using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Kinect.Toolbox;
using Kinect.Toolbox.Record;
using KinectPhotoHack.ApiItem;
using KinectPhotoHack.ClientMethods;
using Microsoft.Research.Kinect.Nui;
using PhotobucketAPI;

namespace KinectPhotoHack
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static UserAuth user;
        private readonly AlgorithmicPostureDetector algorithmicPostureRecognizer = new AlgorithmicPostureDetector();
        private readonly BarycenterHelper barycenterHelper = new BarycenterHelper();

        private readonly Dictionary<JointID, Brush> jointColors = new Dictionary<JointID, Brush>
                                                                      {
                                                                          {
                                                                              JointID.HipCenter,
                                                                              new SolidColorBrush(Color.FromRgb(169, 176,
                                                                                                                155))
                                                                              },
                                                                          {
                                                                              JointID.Spine,
                                                                              new SolidColorBrush(Color.FromRgb(169, 176,
                                                                                                                155))
                                                                              },
                                                                          {
                                                                              JointID.ShoulderCenter,
                                                                              new SolidColorBrush(Color.FromRgb(168, 230,
                                                                                                                29))
                                                                              },
                                                                          {
                                                                              JointID.Head,
                                                                              new SolidColorBrush(Color.FromRgb(200, 0,
                                                                                                                0))
                                                                              },
                                                                          {
                                                                              JointID.ShoulderLeft,
                                                                              new SolidColorBrush(Color.FromRgb(79, 84,
                                                                                                                33))
                                                                              },
                                                                          {
                                                                              JointID.ElbowLeft,
                                                                              new SolidColorBrush(Color.FromRgb(84, 33,
                                                                                                                42))
                                                                              },
                                                                          {
                                                                              JointID.WristLeft,
                                                                              new SolidColorBrush(Color.FromRgb(255, 126,
                                                                                                                0))
                                                                              },
                                                                          {
                                                                              JointID.HandLeft,
                                                                              new SolidColorBrush(Color.FromRgb(215, 86,
                                                                                                                0))
                                                                              },
                                                                          {
                                                                              JointID.ShoulderRight,
                                                                              new SolidColorBrush(Color.FromRgb(33, 79,
                                                                                                                84))
                                                                              },
                                                                          {
                                                                              JointID.ElbowRight,
                                                                              new SolidColorBrush(Color.FromRgb(33, 33,
                                                                                                                84))
                                                                              },
                                                                          {
                                                                              JointID.WristRight,
                                                                              new SolidColorBrush(Color.FromRgb(77, 109,
                                                                                                                243))
                                                                              },
                                                                          {
                                                                              JointID.HandRight,
                                                                              new SolidColorBrush(Color.FromRgb(37, 69,
                                                                                                                243))
                                                                              },
                                                                          {
                                                                              JointID.HipLeft,
                                                                              new SolidColorBrush(Color.FromRgb(77, 109,
                                                                                                                243))
                                                                              },
                                                                          {
                                                                              JointID.KneeLeft,
                                                                              new SolidColorBrush(Color.FromRgb(69, 33,
                                                                                                                84))
                                                                              },
                                                                          {
                                                                              JointID.AnkleLeft,
                                                                              new SolidColorBrush(Color.FromRgb(229, 170,
                                                                                                                122))
                                                                              },
                                                                          {
                                                                              JointID.FootLeft,
                                                                              new SolidColorBrush(Color.FromRgb(255, 126,
                                                                                                                0))
                                                                              },
                                                                          {
                                                                              JointID.HipRight,
                                                                              new SolidColorBrush(Color.FromRgb(181, 165,
                                                                                                                213))
                                                                              },
                                                                          {
                                                                              JointID.KneeRight,
                                                                              new SolidColorBrush(Color.FromRgb(71, 222,
                                                                                                                76))
                                                                              },
                                                                          {
                                                                              JointID.AnkleRight,
                                                                              new SolidColorBrush(Color.FromRgb(245, 228,
                                                                                                                156))
                                                                              },
                                                                          {
                                                                              JointID.FootRight,
                                                                              new SolidColorBrush(Color.FromRgb(77, 109,
                                                                                                                243))
                                                                              }
                                                                      };

        private readonly ColorStreamManager streamManager = new ColorStreamManager();
        private readonly SwipeGestureDetector swipeGestureRecognizer = new SwipeGestureDetector();

        private bool doNextFrameCapture;
        private Runtime kinectRuntime;
        private ObservableMediaCollection list;

        private UrlMedia mediaItem;
        private SkeletonDisplayManager skeletonDisplayManager;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ImageContainer.DataContext = mediaItem;

            var cli = new Client();

            list = ObservableMediaCollection.Create("/search/!/image",
                                                    new Dictionary<string, string>
                                                        {
                                                            {"id", "kittens"},
                                                            {"secondaryperpage", "0"}
                                                        },
                                                    ObservableMediaCollection.RequestType.Search);

            //get first
            list.Fetch(
                delegate(Client.ResponseArgs response)
                    {
                        Debug.WriteLine(response.ToString());

                        mediaItem = list.Collection[0];
                        reloadImage();
                    }
                );

            try
            {
                kinectRuntime = new Runtime();
                kinectRuntime.Initialize(RuntimeOptions.UseSkeletalTracking | RuntimeOptions.UseColor);
                kinectRuntime.VideoStream.Open(ImageStreamType.Video, 2, ImageResolution.Resolution640x480,
                                               ImageType.Color);
                kinectRuntime.SkeletonFrameReady += kinectRuntime_SkeletonFrameReady;
                kinectRuntime.VideoFrameReady += kinectRuntime_VideoFrameReady;

                swipeGestureRecognizer.OnGestureDetected += OnSwipeGestureDetected;

                skeletonDisplayManager = new SkeletonDisplayManager(kinectRuntime.SkeletonEngine, kinectCanvas);

                kinectRuntime.SkeletonEngine.TransformSmooth = true;
                var parameters = new TransformSmoothParameters
                                     {
                                         Smoothing = 1.0f,
                                         Correction = 0.1f,
                                         Prediction = 0.1f,
                                         JitterRadius = 0.05f,
                                         MaxDeviationRadius = 0.05f
                                     };
                kinectRuntime.SkeletonEngine.SmoothParameters = parameters;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void previousItem()
        {
            int currentIndex = list.Collection.IndexOf(mediaItem);
            if (currentIndex > 0)
            {
                mediaItem = list.Collection[currentIndex - 1];
                reloadImage();
            }
        }

        private void nextItem()
        {
            int currentIndex = list.Collection.IndexOf(mediaItem);
            if (currentIndex + 1 < list.Collection.Count())
            {
                mediaItem = list.Collection[currentIndex + 1];
                reloadImage();
            }
        }

        private void reloadImage()
        {
            LayoutRoot.DataContext = null;

            ImageContainer.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

            ImageContainer.DataContext = mediaItem;
        }

        private void kinectRuntime_VideoFrameReady(object sender, ImageFrameReadyEventArgs e)
        {
            kinectDisplay.Source = streamManager.Update(e);

            if (doNextFrameCapture && user != null && String.IsNullOrEmpty(user.Username))
            {
                doNextFrameCapture = false;
                PlanarImage Image = e.ImageFrame.Image;

                BitmapSource bmp = BitmapSource.Create(Image.Width, Image.Height, 96, 96, PixelFormats.Bgr32,
                                                       null, Image.Bits, Image.Width*Image.BytesPerPixel);

                var memstream = new MemoryStream();
                var encoder = new JpegBitmapEncoder
                                  {
                                      FlipHorizontal = true,
                                      FlipVertical = false,
                                      QualityLevel = 70,
                                      Rotation = Rotation.Rotate0
                                  };
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(memstream);
                memstream.Seek(0, SeekOrigin.Begin);
                var cli = new Client
                              {
                                  oauth_secret = user.OAuthTokenSecret,
                                  oauth_token = user.OAuthToken,
                                  Subdomain = user.UserSubdomain
                              };
                cli.uploadItem(new UploadMedia
                                   {
                                       Type = Client.UploadType.image,
                                       Stream = memstream,
                                       Filename = System.Guid.NewGuid().ToString(),
                                   },
                               "jhart_test",
                               UploadCallback);
            }
        }

        private void kinectRuntime_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            if (e.SkeletonFrame.Skeletons.Where(s => s.TrackingState != SkeletonTrackingState.NotTracked).Count() == 0)
                return;

            ProcessFrame(e.SkeletonFrame);
        }

        private void ProcessFrame(ReplaySkeletonFrame frame)
        {
            var stabilities = new Dictionary<int, string>();
            foreach (ReplaySkeletonData skeleton in frame.Skeletons)
            {
                if (skeleton.TrackingState != SkeletonTrackingState.Tracked)
                    continue;

                barycenterHelper.Add(skeleton.Position.ToVector3(), skeleton.TrackingID);

                stabilities.Add(skeleton.TrackingID,
                                barycenterHelper.IsStable(skeleton.TrackingID) ? "Stable" : "Unstable");
                if (!barycenterHelper.IsStable(skeleton.TrackingID))
                    continue;

                foreach (Joint joint in skeleton.Joints)
                {
                    if (joint.Position.W < 0.8f || joint.TrackingState != JointTrackingState.Tracked)
                        continue;

                    if (joint.ID == JointID.HandRight)
                    {
                        swipeGestureRecognizer.Add(joint.Position, kinectRuntime.SkeletonEngine);
                    }
                }

                algorithmicPostureRecognizer.TrackPostures(skeleton);
            }

            skeletonDisplayManager.Draw(frame);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
        }

        public void OnSwipeGestureDetected(string gesture)
        {
            Debug.WriteLine(gesture);

            if (gesture == "SwipeToRight")
            {
                nextItem();
            }
            else if (gesture == "SwipeToLeft")
            {
                previousItem();
            }
        }

        private void Viewbox_MouseUp(object sender, MouseButtonEventArgs e)
        {
            //capture a picture!
            doNextFrameCapture = true;
        }

        public void UploadCallback(Client.ResponseArgs response)
        {
            Debug.WriteLine(response.ResponseXML);
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            var form1 = new Form1();
            form1.Show();
        }
    }

    public class UserAuth
    {
        public string OAuthToken;
        public string OAuthTokenSecret;
        public string UserSubdomain;
        public string Username;
    }
}