# Dopamine Unblock Sidecar

This loopback-only helper uses `@unblockneteasemusic/server` 0.28.0 as an optional playback fallback.
It does not install a system proxy, modify hosts, install certificates, or receive the user's Netease cookie.

The helper is disabled by default. It is only started after an official Netease playback request fails with
a copyright, empty URL, subscription, or trial-only result and the user has enabled the fallback in Settings.

The Dopamine wrapper follows Dopamine's GPL-3.0 license. It is built as a separate process to keep the
`@unblockneteasemusic/server` LGPL-3.0 dependency boundary explicit. Corresponding dependency versions,
reproducible build instructions and LGPL license texts are included with Portable artifacts.

Upstream source: https://github.com/UnblockNeteaseMusic/server (release v0.28.0).
