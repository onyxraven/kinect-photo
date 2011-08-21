using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using PhotobucketAPI;

namespace KinectPhotoHack
{
    public static class DispatcherExtension
    {
        public static void BeginInvokeOn(this Dispatcher dsp, Action a)
        {
            if (!dsp.CheckAccess())
            {
                dsp.BeginInvoke(a);
            }
            else
            {
                a();
            }
        }
    }

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

    public static class StringExtension
    {
        private const string unreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

        public static string UrlEncode3986(this String str)
        {
            var result = new StringBuilder();

            foreach (char symbol in str)
            {
                if (unreservedChars.IndexOf(symbol) != -1)
                {
                    result.Append(symbol);
                }
                else
                {
                    result.Append('%' + String.Format("{0:X2}", (int) symbol));
                }
            }

            return result.ToString();
        }
    }

    public static class IEnumerableKeyValuePairExtension
    {
        private static readonly char[] charsToTrim = {'&', ' '};

        public static string ToQueryString(this IEnumerable<KeyValuePair<string, string>> dict)
        {
            var result = new StringBuilder();
            foreach (var kvp in dict)
            {
                result.AppendFormat("{0}={1}&", kvp.Key.UrlEncode3986(), kvp.Value.UrlEncode3986());
            }
            return result.ToString().Trim(charsToTrim);
        }
    }

    public static class ByteArrayExtension
    {
        public static string ToHexString(this byte[] bytes)
        {
            string result = "";
            foreach (Byte b in bytes)
            {
                result += b.ToString("X2");
            }
            return result;
        }
    }

    public class ItemProgressEventArgs : ProgressChangedEventArgs
    {
        public ItemProgressEventArgs(long total, long current, object state)
            : base((int) ((current/(float) total)*100), state)
        {
            CurrentBytes = current;
            TotalBytes = total;
        }

        public long CurrentBytes { get; protected set; }
        public long TotalBytes { get; protected set; }
    }

    public delegate void ItemProgressEventHandler(object sender, ItemProgressEventArgs e);

    public class ItemTransferCompleteEventArgs : EventArgs
    {
        public ItemTransferCompleteEventArgs(long total)
        {
            TotalBytes = total;
        }

        public long TotalBytes { get; protected set; }
    }

    public delegate void ItemTransferCompleteEventHandler(object sender, ItemTransferCompleteEventArgs e);

    public delegate void ResponseEventHandler(object sender, Client.ResponseArgs response);

    public class BoolToVisibilityConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return Visibility.Collapsed;
            }

            return ((bool) value) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return ((Visibility) value == Visibility.Visible);
        }

        #endregion
    }

    public class StringFormatConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter != null)
            {
                string formatterString = parameter.ToString();

                if (!string.IsNullOrEmpty(formatterString))
                {
                    return string.Format(culture, formatterString, value);
                }
            }

            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public class IntToVisibilityConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
            {
                return Visibility.Collapsed;
            }

            return ((int) value > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}