using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhotobucketAPI.OAuth
{
    public class KeyValuePairComparer : IComparer<KeyValuePair<string, string>>
    {
        #region IComparer<KeyValuePair<string,string>> Members

        public int Compare(KeyValuePair<string, string> x, KeyValuePair<string, string> y)
        {
            if (x.Key == y.Key)
            {
                return string.Compare(x.Value, y.Value,
                                      CultureInfo.InvariantCulture, CompareOptions.Ordinal);
            }
            else
            {
                return string.Compare(x.Key, y.Key,
                                      CultureInfo.InvariantCulture, CompareOptions.Ordinal);
            }
        }

        #endregion
    }

    public static class Utilities
    {
        public const string unreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

        public static char[] charsToTrim = {'&', ' '};

        /// <summary>
        /// stored timestamp offset from the current device time
        /// </summary>
        public static int ServerTimestampOffset = Int32.MinValue;

        public static string UrlEncode3986(string str)
        {
            string result = "";
            if (String.IsNullOrEmpty(str))
            {
                return result;
            }
            foreach (char symbol in str)
            {
                if (unreservedChars.IndexOf(symbol) != -1)
                {
                    result += symbol;
                }
                else
                {
                    result += '%' + String.Format("{0:X2}", (int) symbol);
                }
            }
            return result;
        }

        public static string ToQueryString(IEnumerable<KeyValuePair<string, string>> dict)
        {
            string result = "";
            if (dict == null)
            {
                return result;
            }
            foreach (var kvp in dict)
            {
                result += String.Format("{0}={1}&", UrlEncode3986(kvp.Key), UrlEncode3986(kvp.Value));
            }
            return result.Trim(charsToTrim);
        }

        /// <summary>
        /// Generates a valid nonce string for OAuth
        /// </summary>
        /// <returns>nonce string (a guid)</returns>
        public static string makeNonce()
        {
            return Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Generates unix timestamp, optionally offset by the static value
        /// </summary>
        /// <param name="offset">use the current offset?  default true</param>
        /// <returns>seconds since epoch</returns>
        public static int makeTimestamp(bool offset = true)
        {
            var unixTime = (int) (DateTime.Now.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, 0)).TotalSeconds;
            if (offset)
            {
                unixTime += ServerTimestampOffset;
            }
            return unixTime;
        }
    }
}