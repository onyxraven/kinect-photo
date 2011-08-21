using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using PhotobucketAPI;

namespace KinectPhotoHack.ApiItem
{
    public abstract class AbstractMedia : Abstract
    {
        #region State enum

        [FlagsAttribute]
        public enum State
        {
            NULL = 0x0,
            Pending = 0x1,
            Ready = 0x2,
            Transferring = 0x4,
            Processing = 0x8,
            Finished = 0x10,
            Failed = 0x20,
            PreviouslyUploaded = 0x40,
            Removed = 0x80,
        }

        #endregion

        #region Type enum

        public enum Type
        {
            NULL,
            image,
            video
        }

        #endregion

        protected string _description;
        protected int _progress;
        protected bool _selected;
        protected State _state;
        protected BitmapImage _thumbBitmap;
        protected string _title;
        protected Type _type;
        public abstract BitmapImage thumbBitmap { get; }

        public Type type
        {
            get { return _type; }
            set
            {
                if (CheckPropertyChanged("type", ref _type, ref value))
                {
                    //dunno
                }
            }
        }

        public State state
        {
            get { return _state; }
            set
            {
                if (CheckPropertyChanged("state", ref _state, ref value))
                {
                    //dunno
                }
            }
        }

        public int progress
        {
            get { return _progress; }
            set
            {
                if (CheckPropertyChanged("progress", ref _progress, ref value))
                {
                    //dunno
                }
            }
        }

        public string title
        {
            get { return _title; }
            set
            {
                if (CheckPropertyChanged("title", ref _title, ref value))
                {
                    //dunno
                }
            }
        }

        public string description
        {
            get { return _description; }
            set
            {
                if (CheckPropertyChanged("description", ref _description, ref value))
                {
                    //dunno
                }
            }
        }

        public bool selected
        {
            get { return _selected; }
            set
            {
                if (CheckPropertyChanged("selected", ref _selected, ref value))
                {
                    //dunno
                }
            }
        }

        public event EventHandler OnStart;

        public void FireOnStart(EventArgs e)
        {
            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             state = State.Transferring;
                                             if (OnStart != null)
                                             {
                                                 OnStart(this, e);
                                             }
                                         });
        }


        public event ItemProgressEventHandler OnProgress;

        public void FireOnProgress(ItemProgressEventArgs e)
        {
            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             state = State.Transferring;
                                             progress = e.ProgressPercentage;
                                             if (OnProgress != null)
                                             {
                                                 OnProgress(this, e);
                                             }
                                         });
        }

        public event ItemTransferCompleteEventHandler OnTransferComplete;

        public void FireOnTransferComplete(ItemTransferCompleteEventArgs e)
        {
            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             state = State.Processing;
                                             if (OnTransferComplete != null)
                                             {
                                                 OnTransferComplete(this, e);
                                             }
                                         });
        }

        public event ResponseEventHandler OnComplete;

        public void FireOnComplete(Client.ResponseArgs r)
        {
            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             state = State.Finished;
                                             if (OnComplete != null)
                                             {
                                                 OnComplete(this, r);
                                             }
                                         });
        }

        public event ResponseEventHandler OnFailed;

        public void FireOnFailed(Client.ResponseArgs r)
        {
            Dispatcher.BeginInvokeOn(delegate
                                         {
                                             state = State.Failed;
                                             if (OnFailed != null)
                                             {
                                                 OnFailed(this, r);
                                             }
                                         });
        }
    }

    public class UrlMedia : AbstractMedia
    {
        protected string _albumurl;
        protected string _browseurl;
        protected string _thumb;
        protected string _url;
        protected string _username;

        public string url
        {
            get { return _url; }
            set
            {
                if (CheckPropertyChanged("url", ref _url, ref value))
                {
                    FirePropertyChanged("resizedFullUrl");
                    FirePropertyChanged("resizedSaveUrl");
                    FirePropertyChanged("filename");
                }
            }
        }

        public string resizedFullUrl
        {
            get
            {
                //apply resize rewrite here for full size, 800 px max, no gifs
                return "http://resize.photobucket.com/800x800/a2j/" + OUrlReplace(url);
            }
        }

        public string resizedSaveUrl
        {
            get
            {
                //todo apply resize rewrite here for full size, 2000x2000 px max, all JPEG
                return "http://resize.photobucket.com/2000x2000/a2j/" + OUrlReplace(url);
                //return url;
            }
        }

        public string filename
        {
            get { return url.Split('/').Last(); }
        }


        public string thumb
        {
            get { return _thumb; }
            set
            {
                if (CheckPropertyChanged("thumb", ref _thumb, ref value))
                {
                    FirePropertyChanged("resizedThumbUrl");
                    FirePropertyChanged("resizedThumbUri");
                    _thumbBitmap = null;
                    FirePropertyChanged("thumbBitmap");
                }
            }
        }

        public string resizedThumbUrl
        {
            get
            {
                //apply resize rewrite here for full size, (?) px max, no gifs
                return "http://resize.photobucket.com/104x104/a2j/" + OUrlReplace(thumb);
            }
        }

        public Uri resizedThumbUri
        {
            get
            {
                //apply resize rewrite here for full size, (?) px max, no gifs
                return new Uri(resizedThumbUrl);
            }
        }

        public override BitmapImage thumbBitmap
        {
            get
            {
                if (_thumbBitmap != null)
                {
                    return _thumbBitmap;
                }
                return _thumbBitmap = new BitmapImage(new Uri(resizedThumbUrl));
            }
        }


        public string username
        {
            get { return _username; }
            set
            {
                if (CheckPropertyChanged("username", ref _username, ref value))
                {
                    //dunno
                }
            }
        }

        public string browseurl
        {
            get { return _browseurl; }
            set
            {
                if (CheckPropertyChanged("browseurl", ref _browseurl, ref value))
                {
                    //dunno
                }
            }
        }

        public string albumurl
        {
            get { return _albumurl; }
            set
            {
                if (CheckPropertyChanged("albumurl", ref _albumurl, ref value))
                {
                    FirePropertyChanged("albumSubdomain");
                    FirePropertyChanged("albumId");
                }
            }
        }

        public string albumSubdomain
        {
            get { return albumurl.Remove(0, 7).Split('/').First(); }
        }

        public string albumId
        {
            get
            {
                if (albumurl != null)
                {
                    string[] parts = albumurl.Remove(0, 7).Split('/');
                    string ret = "";
                    for (int i = 3; i < parts.Length; i++)
                    {
                        ret += parts[i] + "/";
                    }
                    return ret.TrimEnd('/');
                }
                else
                {
                    //build from my url - might be a little not as good.
                    string[] parts = url.Remove(0, 7).Split('/');
                    string ret = "";
                    for (int i = 3; i < parts.Length - 1; i++)
                    {
                        ret += parts[i] + "/";
                    }
                    return ret.TrimEnd('/');
                }
            }
        }
    }

    internal class UploadMedia : Client.IUpload
    {
        #region IUpload Members

        /// <summary>
        /// Filename to represent this  media
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// Title for this media (will be set on upload)
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Description for this media (will be set on upload)
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Type of media to be uploaded (must represent the media properly)
        /// </summary>
        public Client.UploadType Type { get; set; }

        /// <summary>
        /// handle to the file data
        /// </summary>
        public Stream Stream { get; set; }

        #endregion
    }
}