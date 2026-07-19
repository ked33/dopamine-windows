#!/usr/bin/env node
'use strict';

const http = require('http');

const PROTOCOL_VERSION = 1;
const UNBLOCK_VERSION = '0.28.0';
const MAX_BODY_BYTES = 64 * 1024;
const IDLE_TIMEOUT_MS = 10 * 60 * 1000;
const token = process.env.DOPAMINE_UNBLOCK_TOKEN || '';
const parentPid = Number.parseInt(process.env.DOPAMINE_PARENT_PID || '0', 10);

if (!token || token.length < 32) {
  process.stderr.write('Missing sidecar authentication token.\n');
  process.exit(2);
}

function normalize(value) {
  return String(value || '')
    .normalize('NFKC')
    .toLowerCase()
    .replace(/[\s\p{P}\p{S}]+/gu, '');
}

function strictSelect(list, info) {
  if (!Array.isArray(list) || !info) return null;
  const expectedDuration = Number(info.duration) || 0;
  const expectedTitle = normalize(info.name);
  const expectedArtists = (info.artists || []).map((artist) => normalize(artist && artist.name)).filter(Boolean);
  const expectedAlbum = normalize(info.album && info.album.name);

  const candidates = list.map((song, index) => {
    const duration = Number(song && song.duration) || 0;
    const title = normalize(song && song.name);
    const artists = (song && song.artists || []).map((artist) => normalize(artist && artist.name)).filter(Boolean);
    const album = normalize(song && song.album && song.album.name);
    const durationTolerance = Math.max(5000, expectedDuration * 0.03);
    const durationDifference = expectedDuration > 0 && duration > 0
      ? Math.abs(duration - expectedDuration)
      : Number.POSITIVE_INFINITY;
    const titleMatches = expectedTitle && title && expectedTitle === title;
    const artistMatches = expectedArtists.length === 0 ||
      (artists.length > 0 && expectedArtists.some((artist) => artists.includes(artist)));
    const albumFallbackMatches = expectedArtists.length > 0 && artists.length === 0 &&
      expectedAlbum && album && expectedAlbum === album;

    if (!titleMatches || (!artistMatches && !albumFallbackMatches) || durationDifference > durationTolerance) return null;
    return { song, score: durationDifference + index * 100 };
  }).filter(Boolean).sort((left, right) => left.score - right.score);

  return candidates.length > 0 ? candidates[0].song : null;
}

strictSelect.ENABLE_FLAC = String(process.env.ENABLE_FLAC || '').toLowerCase() === 'true';

const selectPath = require.resolve('@unblockneteasemusic/server/src/provider/select.js');
require('@unblockneteasemusic/server/src/provider/select.js');
require.cache[selectPath].exports = strictSelect;

process.env.LOG_LEVEL = 'error';
process.env.JSON_LOG = 'true';
process.env.FOLLOW_SOURCE_ORDER = 'true';
const match = require('@unblockneteasemusic/server');

let activeRequest = false;
let idleTimer = null;

function resetIdleTimer() {
  if (idleTimer) clearTimeout(idleTimer);
  idleTimer = setTimeout(() => server.close(() => process.exit(0)), IDLE_TIMEOUT_MS);
  if (typeof idleTimer.unref === 'function') idleTimer.unref();
}

function writeJson(response, statusCode, payload) {
  const body = JSON.stringify(payload);
  response.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body),
    'Cache-Control': 'no-store'
  });
  response.end(body);
}

function readJson(request) {
  return new Promise((resolve, reject) => {
    let length = 0;
    let rejected = false;
    const chunks = [];
    request.on('data', (chunk) => {
      if (rejected) return;
      length += chunk.length;
      if (length > MAX_BODY_BYTES) {
        rejected = true;
        chunks.length = 0;
        reject(new Error('body_too_large'));
        return;
      }
      chunks.push(chunk);
    });
    request.on('end', () => {
      if (rejected) return;
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
      } catch (_) {
        reject(new Error('invalid_json'));
      }
    });
    request.on('error', reject);
  });
}

const server = http.createServer(async (request, response) => {
  resetIdleTimer();

  if (request.socket.remoteAddress !== '127.0.0.1' && request.socket.remoteAddress !== '::ffff:127.0.0.1') {
    writeJson(response, 403, { error: 'loopback_only' });
    return;
  }

  if (request.headers['x-dopamine-sidecar-token'] !== token) {
    writeJson(response, 401, { error: 'unauthorized' });
    return;
  }

  if (request.method === 'GET' && request.url === '/health') {
    writeJson(response, 200, { protocolVersion: PROTOCOL_VERSION, unblockVersion: UNBLOCK_VERSION, status: 'ready' });
    return;
  }

  if (request.method !== 'POST' || request.url !== '/v1/match') {
    writeJson(response, 404, { error: 'not_found' });
    return;
  }

  if (activeRequest) {
    writeJson(response, 429, { error: 'busy' });
    return;
  }

  activeRequest = true;
  try {
    const body = await readJson(request);
    const songIdText = String(body.songId || '');
    const songId = Number.parseInt(songIdText, 10);
    const sources = Array.isArray(body.sources)
      ? [...new Set(body.sources.filter((value) => ['kugou', 'bodian', 'kuwo'].includes(value)))]
      : [];
    const duration = Number(body.durationMilliseconds) || 0;

    if (!/^\d+$/.test(songIdText) || !Number.isSafeInteger(songId) || songId <= 0 ||
      sources.length === 0 || !body.title || duration <= 0 || duration > 24 * 60 * 60 * 1000) {
      writeJson(response, 400, { error: 'invalid_request' });
      return;
    }

    const artistNames = Array.isArray(body.artists) ? body.artists : [];

    const metadata = {
      id: songId,
      name: String(body.title),
      alias: [],
      duration,
      album: { id: 0, name: String(body.album || '') },
      artists: artistNames.map((name, index) => ({ id: index, name: String(name) }))
    };

    const result = await match(songId, sources, metadata);
    if (!result || typeof result.url !== 'string' || !/^https?:\/\//i.test(result.url)) {
      writeJson(response, 404, { error: 'not_found' });
      return;
    }

    const size = Number(result.size) || 0;
    const bitrate = Number(result.br) || 0;
    if (size > 0 && bitrate > 0) {
      const estimatedDuration = size * 8 * 1000 / bitrate;
      const tolerance = Math.max(5000, duration * 0.03);
      if (Math.abs(estimatedDuration - duration) > tolerance) {
        writeJson(response, 409, { error: 'duration_mismatch' });
        return;
      }
    }

    writeJson(response, 200, {
      protocolVersion: PROTOCOL_VERSION,
      url: result.url,
      source: String(result.source || ''),
      bitrate,
      size,
      mediaType: String(result.type || '')
    });
  } catch (error) {
    writeJson(response, 404, { error: error && error.message === 'body_too_large' ? 'body_too_large' : 'not_found' });
  } finally {
    activeRequest = false;
  }
});

server.listen(0, '127.0.0.1', () => {
  const address = server.address();
  process.stdout.write(`DOPAMINE_UNBLOCK_READY ${JSON.stringify({ protocolVersion: PROTOCOL_VERSION, unblockVersion: UNBLOCK_VERSION, port: address.port })}\n`);
  resetIdleTimer();
});

if (parentPid > 0) {
  const parentTimer = setInterval(() => {
    try {
      process.kill(parentPid, 0);
    } catch (_) {
      server.close(() => process.exit(0));
    }
  }, 2000);
  if (typeof parentTimer.unref === 'function') parentTimer.unref();
}

process.on('SIGTERM', () => server.close(() => process.exit(0)));
process.on('SIGINT', () => server.close(() => process.exit(0)));
