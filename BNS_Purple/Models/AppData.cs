using System;
using System.ComponentModel;

namespace BNS_Purple.Models
{
    public class AppData
    {
        public string GamePath
        {
            get { return Properties.Settings.Default.GamePath; }
            set
            {
                Properties.Settings.Default.GamePath = value;
                Properties.Settings.Default.Save();
            }
        }

        public int UpdaterThreads
        {
            get { return Properties.Settings.Default.ThreadCount; }
            set
            {
                Properties.Settings.Default.ThreadCount = (int)value;
                Properties.Settings.Default.Save();
            }
        }

        public int DownloadThreads
        {
            get { return Properties.Settings.Default.DownloadCount; }
            set
            {
                Properties.Settings.Default.DownloadCount = (int)value;
                Properties.Settings.Default.Save();
            }
        }

        public bool IgnoreHashCheck
        {
            get { return Properties.Settings.Default.IgnoreHash; }
            set
            {
                Properties.Settings.Default.IgnoreHash = (bool)value;
                Properties.Settings.Default.Save();
            }
        }

        public string RepositoryServerAddress = string.Empty;
        public string GameId = string.Empty;
    }
}
