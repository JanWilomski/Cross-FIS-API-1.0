
using Cross_FIS_API_1._0.Models;
using Cross_FIS_API_1._0.Views;
using System.Windows.Input;

namespace Cross_FIS_API_1._0.ViewModels
{
    public class InstrumentDetailViewModel
    {
        private readonly FISApiClient _fisApiClient;

        public Instrument BaseInstrument { get; }
        public Instrument CrossInstrument { get; }

        public bool CanCreateCrossOrder => CrossInstrument != null;

        public ICommand OpenCrossOrderWindowCommand { get; }

        public InstrumentDetailViewModel(Instrument baseInstrument, Instrument crossInstrument, FISApiClient fisApiClient)
        {
            BaseInstrument = baseInstrument;
            CrossInstrument = crossInstrument;
            _fisApiClient = fisApiClient;

            OpenCrossOrderWindowCommand = new RelayCommand(OpenCrossOrderWindow, () => CanCreateCrossOrder);
        }

        private void OpenCrossOrderWindow()
        {
            var crossOrderWindow = new CrossOrderWindow();
            crossOrderWindow.DataContext = new CrossOrderViewModel(_fisApiClient, CrossInstrument.GLID);
            crossOrderWindow.Show();
        }
    }
}
