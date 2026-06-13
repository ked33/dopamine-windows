using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dopamine.Core.Extensions
{
    public static class ListExtensions
    {
        private static readonly object randomLock = new object();
        private static readonly Random random = new Random();

        public static string FirstNonEmpty(this IEnumerable<string> strings, string alternateString)
        {
            foreach (string item in strings)
            {
                if (!string.IsNullOrEmpty(item))
                {
                    return item;
                }
            }

            return alternateString;
        }

        public static bool IsNullOrEmpty<T>(this IList<T> list)
        {
            return list == null || list.Count == 0;
        }

        public static List<T> Randomize<T>(this List<T> list)
        {
            var randomList = new List<T>(list); // Create a new list, so no operation performed here affects the original list object.

            lock (randomLock)
            {
                for (int i = randomList.Count - 1; i > 0; i--)
                {
                    int randomIndex = random.Next(0, i + 1);
                    T temp = randomList[i];
                    randomList[i] = randomList[randomIndex];
                    randomList[randomIndex] = temp;
                }
            }

            return randomList;
        }

        public static IEnumerable<string> SortNaturally(this IEnumerable<string> strings)
        {
            return strings.OrderBy(x => Regex.Replace(x, @"\d+", match => match.Value.PadLeft(4, '0')));
        }
    }
}
