using BNS_Purple.Messages;
using BNS_Purple.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BNS_Purple.ViewModels
{
    public partial class SettingsViewModel : ObservableValidator
    {
        private readonly AppData _appData;

        [ObservableProperty]
        private string gamePath;

        [ObservableProperty]
        private System.Windows.Visibility isCancelVisible = System.Windows.Visibility.Visible;

        [ObservableProperty]
        private int updaterThreadCount;

        [ObservableProperty]
        private int downloaderThreadCount;

        [ObservableProperty]
        private bool bIgnoreHash;

        public SettingsViewModel(AppData appData)
        {
            _appData = appData;
            UpdaterThreadCount = _appData.UpdaterThreads;
            DownloaderThreadCount = _appData.DownloadThreads;
            BIgnoreHash = _appData.IgnoreHashCheck;
            GamePath = _appData.GamePath;
        }

        [RelayCommand]
        void ConfirmSettings()
        {
            _appData.GamePath = GamePath;
            _appData.UpdaterThreads = UpdaterThreadCount;
            _appData.DownloadThreads = DownloaderThreadCount;
            _appData.IgnoreHashCheck = bIgnoreHash;

            if (IsCancelVisible != System.Windows.Visibility.Visible)
            {
                IsCancelVisible = System.Windows.Visibility.Visible;
                WeakReferenceMessenger.Default.Send(new NavigationMessage("FirstTime"));
            }
            else
                WeakReferenceMessenger.Default.Send(new NavigationMessage("ExitSetting"));
        }

        [RelayCommand]
        void CancelSettings()
        {
            WeakReferenceMessenger.Default.Send(new NavigationMessage("ExitSetting"));
        }

        [RelayCommand]
        void ChangeGamePath()
        {
            using (var folder = new System.Windows.Forms.FolderBrowserDialog())
            {
                System.Windows.Forms.DialogResult result = folder.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(folder.SelectedPath))
                    GamePath = folder.SelectedPath + "\\";
            }
        }

        [RelayCommand]
        void UILoaded()
        {
            UpdaterThreadCount = _appData.UpdaterThreads;
            DownloaderThreadCount = _appData.DownloadThreads;
            BIgnoreHash = _appData.IgnoreHashCheck;
            GamePath = _appData.GamePath;
        }
    }
}
