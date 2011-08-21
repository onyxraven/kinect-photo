using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using System.Xml.Linq;
using KinectPhotoHack.ApiItem;
using PhotobucketAPI;

namespace KinectPhotoHack.ClientMethods
{
    public class ObservableMediaCollection : DispatcherObject, INotifyPropertyChanged
    {
        #region RequestType enum

        public enum RequestType
        {
            Recent,
            Search,
            Album,
        }

        #endregion

        private const int PAGE_EMPTY = -1;
        private const int TOTAL_EMPTY = -1;
        private const int DEFAULT_PER_PAGE = 40; //picked 40 because portriat = 4x and landscape=5x
        protected int _currentPage = PAGE_EMPTY;

        public int defaultPerPage = DEFAULT_PER_PAGE;
        public Dictionary<string, string> invariantArgs;

        public RequestType requestType;
        public int totalPages = TOTAL_EMPTY;
        public int totalResults = TOTAL_EMPTY;
        public string url;
        public ObservableCollection<UrlMedia> Collection { get; set; }

        public int currentPage
        {
            get { return _currentPage; }
            set
            {
                if (CheckPropertyChanged("currentPage", ref _currentPage, ref value))
                {
                    FirePropertyChanged("hasMorePages");
                }
            }
        }

        public bool hasMorePages
        {
            get { return (currentPage < totalPages); }
        }

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        public static ObservableMediaCollection Create(string url, Dictionary<string, string> invariantArgs,
                                                       RequestType type)
        {
            return new ObservableMediaCollection
                       {
                           url = url,
                           invariantArgs = invariantArgs,
                           currentPage = 0,
                           Collection = new ObservableCollection<UrlMedia>(),
                           requestType = type,
                       };
        }

        //Fetches next page and runs the callback (on the Dispatcher thread).
        public void Fetch(Client.ResponseCallback cb)
        {
            currentPage++;

            var args = new Dictionary<string, string>(invariantArgs);
            args["page"] = currentPage.ToString();
            args["perpage"] = defaultPerPage.ToString();

            new Client().makeQueryStringRequest(Client.RequestMethods.GET,
                                                url,
                                                args,
                                                Fetch_Complete,
                                                cb
                );
        }

        protected void Fetch_Complete(Client.ResponseArgs response)
        {
            if (response.Error != null)
            {
                Dispatcher.BeginInvokeOn(delegate
                                             {
                                                 var cb = (Client.ResponseCallback) response.Context;
                                                 cb(response);
                                             });
                return;
            }

            XElement contents = response.ResponseXML;

            totalItemsParser(contents);

            IEnumerable<UrlMedia> itemsEnum = from media in contents.Descendants("media")
                                              select new UrlMedia
                                                         {
                                                             url = media.Element("url").Value,
                                                             thumb = media.Element("thumb").Value,
                                                             title = media.Element("title").Value,
                                                             description = media.Element("description").Value,
                                                             username = media.Attribute("username").Value,
                                                             type =
                                                                 (AbstractMedia.Type)
                                                                 Enum.Parse(typeof (AbstractMedia.Type),
                                                                            media.Attribute("type").Value, true),
                                                             browseurl = media.Element("browseurl").Value,
                                                             albumurl =
                                                                 (media.Element("albumurl") == null)
                                                                     ? null
                                                                     : media.Element("albumurl").Value
                                                         };

            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             foreach (UrlMedia item in itemsEnum)
                                             {
                                                 Collection.Add(item);
                                             }

                                             var cb = (Client.ResponseCallback) response.Context;
                                             cb(response);
                                         });
        }

        public void Clear()
        {
            currentPage = PAGE_EMPTY;
            totalResults = TOTAL_EMPTY;
            totalPages = TOTAL_EMPTY;

            Collection.Clear();
        }

        protected void totalItemsParser(XElement contents)
        {
            switch (requestType)
            {
                case RequestType.Album:
                    int photoCount = Convert.ToInt32(contents.Element("album").Attribute("photo_count").Value);
                    totalResults = photoCount; //plus video count
                    totalPages = (int) Math.Ceiling(totalResults/(double) defaultPerPage);
                    break;
                case RequestType.Recent:
                    totalResults = 100;
                    totalPages = (int) Math.Ceiling(totalResults/(double) defaultPerPage);
                    break;
                case RequestType.Search:
                    totalResults = Convert.ToInt32(contents.Element("result").Attribute("totalresults").Value);
                    totalPages = Convert.ToInt32(contents.Element("result").Attribute("totalpages").Value);
                    break;
            }
            FirePropertyChanged("hasMorePages");
        }

        protected bool CheckPropertyChanged<T>(string propertyName, ref T oldValue, ref T newValue)
        {
            if (oldValue == null && newValue == null)
            {
                return false;
            }

            if ((oldValue == null && newValue != null) || !oldValue.Equals(newValue))
            {
                oldValue = newValue;
                FirePropertyChanged(propertyName);
                return true;
            }

            return false;
        }

        protected void FirePropertyChanged(string propertyName)
        {
            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             if (PropertyChanged != null)
                                             {
                                                 PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
                                             }
                                         });
        }
    }
}