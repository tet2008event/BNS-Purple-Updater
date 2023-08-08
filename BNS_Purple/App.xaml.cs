using BNS_Purple.Models;
using BNS_Purple.ViewModels;
using BNS_Purple.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace BNS_Purple
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            IServiceCollection services = new ServiceCollection();

            services.AddSingleton<MainWindowViewModel>().
                AddSingleton<NCService>().
                AddSingleton<AppData>().
                AddSingleton<ProgressView>().
                AddSingleton<ProgressViewModel>().
                AddSingleton<SettingsView>().
                AddSingleton<SettingsViewModel>().
                AddSingleton<UpdaterView>().
                AddSingleton<UpdaterViewModel>().
                AddSingleton<httpClient>().
                AddSingleton(s => new MainWindowView()
                {
                    DataContext = s.GetRequiredService<MainWindowViewModel>()
                });

            _serviceProvider = services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            MainWindow = _serviceProvider.GetRequiredService<MainWindowView>();
            MainWindow.Show();
            Task.Run(async () => await _serviceProvider.GetRequiredService<MainWindowViewModel>().InitializeAsync());
            base.OnStartup(e);
        }
    }
}
