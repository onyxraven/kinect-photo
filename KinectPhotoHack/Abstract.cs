using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace KinectPhotoHack.ApiItem
{
    public abstract class Abstract : DispatcherObject, INotifyPropertyChanged
    {
        public static bool fireChanged = true;
        protected static Regex ourlRegex = new Regex(@"^http://(i|th)");

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        protected bool CheckPropertyChanged<T>(string propertyName, ref T oldValue, ref T newValue)
        {
            if (oldValue == null && newValue == null)
            {
                return false;
            }

            if (((oldValue == null && newValue != null) || !oldValue.Equals(newValue)))
            {
                oldValue = newValue;
                FirePropertyChanged(propertyName);
                return true;
            }

            return false;
        }

        protected void FirePropertyChanged(string propertyName)
        {
            if (PropertyChanged != null && fireChanged)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public static string OUrlReplace(string url)
        {
            return ourlRegex.Replace(url, @"http://oi");
        }
    }

    public class ApiNullItem : Abstract
    {
        //this is just an empty item if needed.
    }
}