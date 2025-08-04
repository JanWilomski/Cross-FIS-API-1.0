using Cross_FIS_API_1._0.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Cross_FIS_API_1._0.ViewModels
{
    public class CrossOrderViewModel : INotifyPropertyChanged
    {
        private readonly FISApiClient _fisApiClient;
        private readonly CrossOrder _crossOrder;

        public CrossOrderViewModel(FISApiClient fisApiClient, string instrumentId = null)
        {
            _fisApiClient = fisApiClient;
            _crossOrder = new CrossOrder();
            if (!string.IsNullOrEmpty(instrumentId))
            {
                _crossOrder.InstrumentId = instrumentId;
            }
            SubmitCrossOrderCommand = new RelayCommand(SubmitCrossOrder, () => CanSubmit);
        }

        public string InstrumentId
        {
            get => _crossOrder.InstrumentId;
            set
            {
                if (_crossOrder.InstrumentId != value)
                {
                    _crossOrder.InstrumentId = value;
                    OnPropertyChanged();
                    ((RelayCommand)SubmitCrossOrderCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string Quantity
        {
            get => _crossOrder.Quantity.ToString();
            set
            {
                if (int.TryParse(value, out int quantity))
                {
                    if (_crossOrder.Quantity != quantity)
                    {
                        _crossOrder.Quantity = quantity;
                        OnPropertyChanged();
                        ((RelayCommand)SubmitCrossOrderCommand).RaiseCanExecuteChanged();
                    }
                }
            }
        }

        public string Price
        {
            get => _crossOrder.Price.ToString();
            set
            {
                if (decimal.TryParse(value, out decimal price))
                {
                    if (_crossOrder.Price != price)
                    {
                        _crossOrder.Price = price;
                        OnPropertyChanged();
                        ((RelayCommand)SubmitCrossOrderCommand).RaiseCanExecuteChanged();
                    }
                }
            }
        }

        public string BuyerAccount
        {
            get => _crossOrder.BuyerAccount;
            set
            {
                if (_crossOrder.BuyerAccount != value)
                {
                    _crossOrder.BuyerAccount = value;
                    OnPropertyChanged();
                    ((RelayCommand)SubmitCrossOrderCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string SellerAccount
        {
            get => _crossOrder.SellerAccount;
            set
            {
                if (_crossOrder.SellerAccount != value)
                {
                    _crossOrder.SellerAccount = value;
                    OnPropertyChanged();
                    ((RelayCommand)SubmitCrossOrderCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand SubmitCrossOrderCommand { get; }

        public bool CanSubmit
        {
            get
            {
                return !string.IsNullOrEmpty(_crossOrder.InstrumentId) &&
                       _crossOrder.Quantity > 0 &&
                       _crossOrder.Price > 0 &&
                       !string.IsNullOrEmpty(_crossOrder.BuyerAccount) &&
                       !string.IsNullOrEmpty(_crossOrder.SellerAccount);
            }
        }

        private void SubmitCrossOrder()
        {
            _fisApiClient.SendCrossOrder(_crossOrder);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}