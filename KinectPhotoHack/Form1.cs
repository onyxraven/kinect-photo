using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Xml.Linq;
using PhotobucketAPI;

namespace KinectPhotoHack
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            performLogin(uname.Text, passwd.Text, logincallback);
        }

        private void logincallback(Client.ResponseArgs response)
        {
            Invoke(new EventHandler(delegate { Close(); }));
        }

        public void performLogin(string username, string password, Client.ResponseCallback cb)
        {
            var client = new Client();
            client.makePost("/login/direct/!",
                            new Dictionary<string, string>
                                {
                                    {"aid", username},
                                    {"password", password}
                                },
                            delegate(Client.ResponseArgs response)
                                {
                                    if (response.Error != null)
                                    {
                                        cb(response);
                                        return;
                                    }
                                    XElement contents = response.ResponseXML;

                                    MainWindow.user = new UserAuth
                                                          {
                                                              Username = contents.Element("username").Value,
                                                              UserSubdomain = contents.Element("subdomain").Value,
                                                              OAuthToken = contents.Element("oauth_token").Value,
                                                              OAuthTokenSecret =
                                                                  contents.Element("oauth_token_secret").Value
                                                          };

                                    // let the caller know we've finished up
                                    cb(response);
                                }
                );
        }
    }
}