using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace phantom.WPF.FileCopy
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        // Implement the PropertyChanged event
        public event PropertyChangedEventHandler? PropertyChanged;
        // Helper method to raise the PropertyChanged event
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        bool isMoveDrive = false;
        int bigFileSize = 9138176;
        int maxMumOfThread = 8;
        private enum MoveFileStatus
        {
            None,
            Processing,
            Done
        }
        private class MoveFileInfo
        {
            public string? Path { get; set; }
            public MoveFileStatus Status { get; set; }
            public long? Length { get; set; }
        }
        private List<MoveFileInfo>? _moveFiles;
        private string? _fromDirectoryTextBox;
        private string? _toDirectoryTextBox;
        private bool _moveButtonIsEnabled = true;
        public bool MoveButtonIsEnabled
        {
            get
            {
                return _moveButtonIsEnabled;
            }
            set
            {
                _moveButtonIsEnabled = value;
                OnPropertyChanged(nameof(MoveButtonIsEnabled));
            }
        }
        private double _mainProgressBarValue = 0;
        public double MainProgressBarValue
        {
            get
            {
                return _mainProgressBarValue;
            }
            set
            {
                _mainProgressBarValue = value;
                OnPropertyChanged(nameof(MainProgressBarValue));
            }
        }
        private ObservableCollection<MoveFileProgress> _moveFileProgresses;
        public ObservableCollection<MoveFileProgress> MoveFileProgresses
        {
            get
            {
                return _moveFileProgresses;
            }
            set
            {
                _moveFileProgresses = value;
                OnPropertyChanged(nameof(MoveFileProgresses));
            }
        }
        private bool _isReplace = false;
        public bool IsReplace
        {
            get => _isReplace;
            set
            {
                _isReplace = value;
                OnPropertyChanged(nameof(IsReplace));
            }
        }
        private void MoveAcrossVolumesWithProgress(string sourceFile, string destinationFile
            , MoveFileProgress moveFileProgress)
        {
            long totalBytes = new FileInfo(sourceFile).Length;
            long bytesCopied = 0;
            byte[] buffer = new byte[4096]; // Adjust buffer size as needed

            using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
            using (var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write))
            {
                int bytesRead;
                while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    destinationStream.Write(buffer, 0, bytesRead);
                    bytesCopied += bytesRead;
                    //int progressPercentage = (int)((double)bytesCopied / totalBytes * 100);
                    //Console.WriteLine($"Progress: {progressPercentage}%"); // Report progress
                    moveFileProgress.ProgressPercentage = (int)((double)bytesCopied / totalBytes * 100);
                    OnPropertyChanged(nameof(MoveFileProgresses));
                }
            }
            File.Delete(sourceFile); // Delete the original after successful copy
        }
        private async void CreateMoveThread()
        {
            await Task.Run(async () =>
            {
                MoveFileInfo processFile;
                lock (_moveFiles!)
                {
                    if (_moveFiles!.Count(x => x.Status == MoveFileStatus.Processing) + 1 >= maxMumOfThread) return;
                    if (_moveFiles!.Any(x => x.Status == MoveFileStatus.None) == false)
                    {
                        if (_moveFiles!.All(x => x.Status == MoveFileStatus.Done))
                        {
                            var files = Directory.GetFiles(_fromDirectoryTextBox!, "*.*", SearchOption.AllDirectories);
                            if (files.Length == 0)
                            {
                                Directory.Delete(_fromDirectoryTextBox!, true);
                            }
                            IsReplace = false;
                            MoveButtonIsEnabled = true;
                        }
                        return;
                    }
                    processFile = _moveFiles.FirstOrDefault(x => x.Status == MoveFileStatus.None)!;
                    processFile!.Status = MoveFileStatus.Processing;
                }
                CreateMoveThread();
                var toFilePath = processFile.Path!.Replace(_fromDirectoryTextBox!, _toDirectoryTextBox);
                Debug.WriteLine($"Moving file {processFile.Path} into {toFilePath}");
                var dirPath = Path.GetDirectoryName(toFilePath);
                if (Directory.Exists(dirPath) == false) Directory.CreateDirectory(dirPath!);
                var moveFileProgress = new MoveFileProgress();
                moveFileProgress.FileName = processFile.Path;
                var fileInfo = new FileInfo(processFile.Path);
                processFile.Length = fileInfo.Length;
                moveFileProgress.Length = fileInfo.Length;
                Dispatcher.Invoke(() =>
                {
                    MoveFileProgresses.Add(moveFileProgress);
                });
                OnPropertyChanged(nameof(MoveFileProgresses));
                if (IsReplace && File.Exists(toFilePath)) File.Delete(toFilePath);
                if (isMoveDrive && processFile.Length > bigFileSize)
                {
                    MoveAcrossVolumesWithProgress(processFile.Path, toFilePath, moveFileProgress);
                }
                else File.Move(processFile.Path, toFilePath);
                moveFileProgress.ProgressPercentage = 100;
                OnPropertyChanged(nameof(MoveFileProgresses));
                Dispatcher.Invoke(() =>
                {
                    MoveFileProgresses.Remove(moveFileProgress);
                });
                OnPropertyChanged(nameof(MoveFileProgresses));
                Debug.WriteLine($"Moved file {processFile.Path} into {toFilePath}");
                processFile!.Status = MoveFileStatus.Done;
                MainProgressBarValue += 1;
                await Task.CompletedTask;
                CreateMoveThread();
            });
        }
        private void OpenFolderDialog(System.Windows.Controls.TextBox textBox)
        {
            var openFolderDialog = new OpenFolderDialog();
            openFolderDialog.Multiselect = false;
            openFolderDialog.ShowDialog();
            textBox.Text = openFolderDialog.FolderName;
        }

        private void Move_Clicked(object sender, RoutedEventArgs e)
        {
            _fromDirectoryTextBox = FromDirectoryTextBox.Text;
            _moveFiles = Directory.GetFiles(_fromDirectoryTextBox, "*.*", SearchOption.AllDirectories).Select(x => new MoveFileInfo
            {
                Path = x,
                Status = MoveFileStatus.None,
            }).ToList();
            MoveFileProgresses = new ObservableCollection<MoveFileProgress>();
            MainProgressBarValue = 0;
            MainProgressBar.Maximum = _moveFiles.Count();
            var folderName = _fromDirectoryTextBox.Split(@"\").Last();
            _toDirectoryTextBox = Path.Combine(ToDirectoryTextBox.Text, folderName);
            if (Directory.Exists(_toDirectoryTextBox) == false) Directory.CreateDirectory(_toDirectoryTextBox);
            MoveButtonIsEnabled = false;
            isMoveDrive = _fromDirectoryTextBox.Substring(0, _fromDirectoryTextBox.IndexOf(":\\")) != _toDirectoryTextBox.Substring(0, _toDirectoryTextBox.IndexOf(":\\"));
            CreateMoveThread();
        }

        private void FromBrowse_Clicked(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog(FromDirectoryTextBox);
        }

        private void ToBrowse_Clicked(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog(ToDirectoryTextBox);
        }
    }
    public class MoveFileProgress : INotifyPropertyChanged
    {

        // Implement the PropertyChanged event
        public event PropertyChangedEventHandler? PropertyChanged;
        // Helper method to raise the PropertyChanged event
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string? _fileName;
        public string? FileName
        {
            get => _fileName; set
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }
        private int? _progressPercentage;
        public int? ProgressPercentage
        {
            get => _progressPercentage; set
            {
                _progressPercentage = value;
                OnPropertyChanged(nameof(ProgressPercentage));
            }
        }
        private long? _length;
        public long? Length
        {
            get => _length; set
            {
                _length = value;
                OnPropertyChanged(nameof(Length));
            }
        }
    }
}