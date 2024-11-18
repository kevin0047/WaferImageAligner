using MongoDB.Bson;
using MongoDB.Driver;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WaferImageAligner.Models;
using OpenCvSharp;
namespace WaferImageAligner.ViewModels

{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private WaferImage _waferImage;
        private BitmapSource _displayImage;
        private StringBuilder _logBuilder = new StringBuilder();
        private readonly System.Timers.Timer _processingTimer;
        private bool _isProcessing;
        private readonly IMongoDatabase _database;
        private readonly string _sourceFolder = @"C:\GeneratedImages";
        private readonly string _alignedFolder = @"C:\AlignImages";
        public ICommand StartProcessingCommand { get; }  // 추가

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

        public MainViewModel()
        {
            // MongoDB 연결 설정
            var client = new MongoClient("mongodb://localhost:27017");
            _database = client.GetDatabase("WaferScanDB");

            // 얼라인 이미지 저장 폴더 생성
            if (!Directory.Exists(_alignedFolder))
            {
                Directory.CreateDirectory(_alignedFolder);
            }

            _processingTimer = new System.Timers.Timer(30000); // 2초
            _processingTimer.Elapsed += ProcessingTimer_Elapsed;
            StartProcessingCommand = new RelayCommand(StartProcessing); // 추가

            AddLog("시스템 초기화 완료");

        }
        private async void StartProcessing() // 추가
        {
            AddLog("작업 시작...");
            await ProcessExistingImages();
            _processingTimer.Start();
            AddLog("자동 모니터링 시작됨");
        }
        private async Task ProcessExistingImages()
        {
            try
            {
                AddLog("기존 이미지 처리 시작");
                var imagesCollection = _database.GetCollection<ImageDocument>("Images");
                var alignedCollection = _database.GetCollection<AlignedImageDocument>("AlignedImages");

                // 처리되지 않은 모든 이미지 검색
                var processedImages = await alignedCollection
                    .Find(_ => true)
                    .Project(x => x.OriginalImageName)
                    .ToListAsync();

                var allImages = await imagesCollection.Find(_ => true).ToListAsync();
                var unprocessedImages = allImages.Where(img => !processedImages.Contains(img.SavedName)).ToList();

                AddLog($"처리할 이미지 {unprocessedImages.Count}개 발견");

                foreach (var image in unprocessedImages)
                {
                    await ProcessSingleImage(image);
                }

                AddLog("기존 이미지 처리 완료");
            }
            catch (Exception ex)
            {
                AddLog($"기존 이미지 처리 중 오류 발생: {ex.Message}");
            }
        }

        private async void ProcessingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_isProcessing) return;

            try
            {
                _isProcessing = true;
                await CheckNewImages();
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task CheckNewImages()
        {
            try
            {
                var imagesCollection = _database.GetCollection<ImageDocument>("Images");
                var alignedCollection = _database.GetCollection<AlignedImageDocument>("AlignedImages");

                // 마지막 처리 시간 확인
                var lastProcessed = await alignedCollection
                    .Find(FilterDefinition<AlignedImageDocument>.Empty)
                    .SortByDescending(x => x.ProcessedTime)
                    .FirstOrDefaultAsync();

                DateTime lastProcessTime = lastProcessed?.ProcessedTime ?? DateTime.MinValue;

                // 새 이미지 검색
                var newImages = await imagesCollection
                    .Find(x => x.GeneratedTime > lastProcessTime)
                    .ToListAsync();

                if (newImages.Count > 0)
                {
                    AddLog($"새로운 이미지 {newImages.Count}개 발견");
                    foreach (var image in newImages)
                    {
                        await ProcessSingleImage(image);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"새 이미지 확인 중 오류: {ex.Message}");
            }
        }

        private async Task ProcessSingleImage(ImageDocument image)
        {
            try
            {
                string sourcePath = Path.Combine(_sourceFolder, image.SavedName);
                if (!File.Exists(sourcePath))
                {
                    AddLog($"이미지 파일 없음: {sourcePath}");
                    return;
                }

                AddLog($"처리 시작: {image.SavedName}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _waferImage = new WaferImage(sourcePath);
                    _waferImage.LogMessage += OnLogMessage;
                    DisplayImage = _waferImage.GetBitmapSource();

                    // 이미지 처리
                    _waferImage.ProcessImage();
                    DisplayImage = _waferImage.GetBitmapSource();
                });

                // 처리된 이미지 저장
                string alignedFileName = $"aligned_{Guid.NewGuid()}.png";
                string alignedPath = Path.Combine(_alignedFolder, alignedFileName);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Cv2.ImWrite(alignedPath, _waferImage.ProcessedImage);
                });

                // DB에 결과 저장
                var alignedCollection = _database.GetCollection<AlignedImageDocument>("AlignedImages");
                var alignedDoc = new AlignedImageDocument
                {
                    OriginalImageName = image.SavedName,
                    AlignedImageName = alignedFileName,
                    ProcessedTime = DateTime.UtcNow
                };

                await alignedCollection.InsertOneAsync(alignedDoc);
                AddLog($"처리 완료: {alignedFileName}");
            }
            catch (Exception ex)
            {
                AddLog($"이미지 처리 중 오류 발생 ({image.SavedName}): {ex.Message}");
            }
        }

        private void OnLogMessage(object sender, string message)
        {
            AddLog(message);
        }

        private void AddLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _logBuilder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                OnPropertyChanged(nameof(LogText));
            });
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ImageDocument
    {
        public ObjectId Id { get; set; }
        public string OriginalName { get; set; }
        public string SavedName { get; set; }
        public DateTime GeneratedTime { get; set; }
    }

    public class AlignedImageDocument
    {
        public ObjectId Id { get; set; }
        public string OriginalImageName { get; set; }
        public string AlignedImageName { get; set; }
        public DateTime ProcessedTime { get; set; }
    }
}