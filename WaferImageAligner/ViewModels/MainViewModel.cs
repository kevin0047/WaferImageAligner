using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WaferImageAligner.Models;
using System.Text;

namespace WaferImageAligner.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private WaferImage _waferImage;
        private BitmapSource _displayImage;
        private StringBuilder _logBuilder = new StringBuilder();

        public BitmapSource DisplayImage
        {
            get => _displayImage;
            set
            {
                _displayImage = value;
                OnPropertyChanged(nameof(DisplayImage));
            }
        }

        public string LogText => _logBuilder.ToString();

        public ICommand LoadImageCommand { get; }
        public ICommand ProcessImageCommand { get; }

        public MainViewModel()
        {
            LoadImageCommand = new RelayCommand(LoadImage);
            ProcessImageCommand = new RelayCommand(ProcessImage, () => _waferImage != null);
        }

        private void LoadImage()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg)|*.png;*.jpeg;*.jpg|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                _waferImage = new WaferImage(openFileDialog.FileName);
                _waferImage.LogMessage += OnLogMessage;
                DisplayImage = _waferImage.GetBitmapSource();
                AddLog($"Image loaded: {openFileDialog.FileName}");
            }
        }

        private void ProcessImage()
        {
            _waferImage.ProcessImage();
            DisplayImage = _waferImage.GetBitmapSource();
        }

        private void OnLogMessage(object sender, string message)
        {
            AddLog(message);
        }

        private void AddLog(string message)
        {
            _logBuilder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            OnPropertyChanged(nameof(LogText));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(); // 수정된 부분

        public void Execute(object parameter) => _execute();
    }
}