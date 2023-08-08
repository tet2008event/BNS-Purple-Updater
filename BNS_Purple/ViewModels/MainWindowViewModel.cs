using BNS_Purple.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using BNS_Purple.Extensions;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using BNS_Purple.Messages;

namespace BNS_Purple.ViewModels
{
    public partial class MainWindowViewModel : ObservableValidator, IRecipient<NavigationMessage>
    {
        private readonly NCService _ncService;
        private readonly IServiceProvider _serviceProvider;
        private bool _isAppReady = false;

        public MainWindowViewModel(NCService ncService, IServiceProvider serviceProvider)
        {
            _ncService = ncService;
            _serviceProvider = serviceProvider;
            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        [ObservableProperty]
        private object currentView;

        [ObservableProperty]
        private WindowState currentWindowState;

        [RelayCommand]
        void CloseWindow() => Application.Current.Shutdown();

        [RelayCommand]
        void MinimizeWindow() => CurrentWindowState = WindowState.Minimized;

        [RelayCommand]
        void OpenSettings()
        {
            if (_isAppReady)
                CurrentView = _serviceProvider.GetRequiredService<SettingsViewModel>();
        }

        public async Task InitializeAsync()
        {
            CurrentView = _serviceProvider.GetRequiredService<ProgressViewModel>();
            var appData = _serviceProvider.GetRequiredService<AppData>();
            await Task.Delay(1000);

            // First time use
            if (appData.GamePath.IsNullOrEmpty())
            {
                _serviceProvider.GetRequiredService<ProgressViewModel>().StatusText = "Preparing first time use";
                
                await Task.Delay(1000);
                CurrentView = _serviceProvider.GetRequiredService<SettingsViewModel>();
                _serviceProvider.GetRequiredService<SettingsViewModel>().IsCancelVisible = Visibility.Collapsed;
            } else
            {
                _serviceProvider.GetRequiredService<UpdaterViewModel>().Initialize();
                CurrentView = _serviceProvider.GetRequiredService<UpdaterViewModel>();
                _isAppReady = true;
            }
        }

        public async Task ConfirmSetup()
        {
            _serviceProvider.GetRequiredService<ProgressViewModel>().StatusText = "I just want you to look at this";
            await Task.Delay(3000);
            _serviceProvider.GetRequiredService<UpdaterViewModel>().Initialize();
            CurrentView = _serviceProvider.GetRequiredService<UpdaterViewModel>();
            _isAppReady = true;
        }

        void IRecipient<NavigationMessage>.Receive(NavigationMessage message)
        {
            switch (message.Value)
            {
                case "FirstTime":
                    CurrentView = _serviceProvider.GetRequiredService<ProgressViewModel>();
                    Task.Run(async () => await ConfirmSetup());
                    break;
                case "ExitSetting":
                    CurrentView = _serviceProvider.GetRequiredService<UpdaterViewModel>();
                    Task.Run(() => _serviceProvider.GetRequiredService<UpdaterViewModel>().UpdateView());
                    break;
                default:
                    break;
            }
        }
    }
}
