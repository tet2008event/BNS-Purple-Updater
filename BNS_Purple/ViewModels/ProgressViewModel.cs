using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BNS_Purple.ViewModels
{
    public partial class ProgressViewModel : ObservableObject
    {
        [ObservableProperty]
        private string statusText;

        public ProgressViewModel()
        {
            StatusText = "Loading...";
        }
    }
}
