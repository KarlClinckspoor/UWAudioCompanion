using Microsoft.Win32;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
// using System.Windows.Shapes;

namespace TestAudioMonitor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static string? _pathToConfigFile;
    private static string? _pathToUwFile;
    private static Dictionary<int, string>? _songPaths;
    private static FileSystemWatcher? _watcher;
    private const string PathToPreviousConfig = "previous_settings.txt";
    private static WaveOutEvent? _outputDevice;
    private static AudioFileReader? _audioFile;
    private static int _previousSongId = -1;
    private static bool _stopPlaying = false;
    public MainWindow()
    {
        InitializeComponent();
        if (!Path.Exists(PathToPreviousConfig)) return;
        var content = File.ReadAllLines(PathToPreviousConfig);
        Debug.Assert(content.Length == 2);
        _pathToConfigFile = content[0];
        _pathToUwFile = content[1];
        if (Path.Exists(_pathToConfigFile) & (Path.Exists(_pathToUwFile)))
        {
            EventsList.Items.Add($"Loaded previous settings from {Path.GetFullPath(PathToPreviousConfig)}");
            EventsList.Items.Add($"Loaded song config file from {Path.GetFullPath(_pathToConfigFile)}");
            EventsList.Items.Add($"Loaded UW file from {Path.GetFullPath(_pathToUwFile)}");
            LoadJsonIntoDict(_pathToConfigFile);
        }
        else
        {
            EventsList.Items.Add($"Tried loading previous settings from {Path.GetFullPath(PathToPreviousConfig)} but some of the files referenced couldn't be found");
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if ((_pathToUwFile is null) | (_pathToUwFile == ""))
        {
            MessageBox.Show("Before proceeding, please specify a path to the UW file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if ((_pathToConfigFile is null) | (_pathToConfigFile == ""))
        {
            MessageBox.Show("Please specify a path to the config file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (_songPaths is null)
        {
            MessageBox.Show("Song Paths weren't loaded properly!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_pathToUwFile), Path.GetFileName(_pathToUwFile));
        _watcher.NotifyFilter = NotifyFilters.LastWrite;
        _watcher.Changed += OnFileChanged;
        _watcher.EnableRaisingEvents = true;

        var writer = File.CreateText(PathToPreviousConfig);
        writer.Write(_pathToConfigFile);
        writer.Write('\n');
        writer.Write(_pathToUwFile);
        writer.Flush();
        writer.Close();
        _stopPlaying = false;
    }

    private void UWFile_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select the path to the UW file",
            Filter = "All Files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        var result = openFileDialog.ShowDialog();
        if (result == true)
        {
            _pathToUwFile = openFileDialog.FileName;
            EventsList.Items.Add($"UW file loaded from {Path.GetFullPath(_pathToUwFile)}");
        }
        else
        {
            MessageBox.Show("Failed to open file");
        }
    }

    private void ConfigFile_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select the path to the config file",
            Filter = "All Files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        var result = openFileDialog.ShowDialog();
        if (result == true)
        {
            _pathToConfigFile = openFileDialog.FileName;
            EventsList.Items.Add($"Song config file loaded from {Path.GetFullPath(_pathToConfigFile)}");
        }
        else
        {
            MessageBox.Show("Failed to open file");
            return;
        }
        LoadJsonIntoDict(_pathToConfigFile);
    }

    private void LoadJsonIntoDict(string pathToConfigFile)
    {
        var jsonString = File.ReadAllText(pathToConfigFile);
        try
        {
            _songPaths = JsonSerializer.Deserialize<Dictionary<int, string>>(jsonString);
        }
        catch (JsonException)
        {
            MessageBox.Show("Failed to load JSON content", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (_songPaths is null)
        {
            MessageBox.Show("Failed to load JSON content (2)", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        foreach (var p in _songPaths)
        {
            if (!File.Exists(p.Value))
            {
                MessageBox.Show($"Could not find song at path {p.Value}. Please fix the JSON file");
                _songPaths = null;
                return;
            }
            EventsList.Items.Add($"Song number {p.Key} bound to {p.Value}");
        }

    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _outputDevice?.Stop();
        CurrentSongLabel.Content = string.Empty;
        _stopPlaying = true;
    }


    // TODO: Since this gets accessed by another thread, not the main thread, any references to widgets have to be removed
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_songPaths is null)
        {
            MessageBox.Show("Song paths dict is null... Bug");
            return;
        }

        var content = File.ReadAllText(e.FullPath);
        int.TryParse(content, out int trackNumber);

        // Don't change song if it isn't a different track
        if (_previousSongId == trackNumber)
        {
            return;
        }

        var songPath = _songPaths.GetValueOrDefault(trackNumber);
        if (string.IsNullOrEmpty(songPath))
        {
            // EventsList.Items.Add($"Couldn't play song for track number {trackNumber}");
            return;
        }
        PlaySong(songPath);
    }

    private void PlaySong(string songPath)
    {
        if (string.IsNullOrEmpty(songPath))
        {
            return;
        }

        _outputDevice ??= new WaveOutEvent();

        _audioFile = new AudioFileReader(songPath);
        _outputDevice.Init(_audioFile);
        _outputDevice.Play();
        CurrentSongLabel.Content = $"Playing: {Path.GetFileNameWithoutExtension(songPath)}";
        _outputDevice.PlaybackStopped += ResetSong;
        _stopPlaying = false;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if ((_songPaths is not null) & (_pathToUwFile is not null))
        {
            string content = File.ReadAllText(_pathToUwFile);
            int.TryParse(content, out int trackNumber);


            var songPath = _songPaths.GetValueOrDefault(trackNumber);
            if (string.IsNullOrEmpty(songPath))
            {
                return;
            }
            PlaySong(songPath);
        }
    }

    private void ResetSong(object? sender, StoppedEventArgs e)
    {
        if (_stopPlaying)
        {
            return;
        }
        if ((_outputDevice is not null) & (_audioFile is not null))
        {
            if (_outputDevice.PlaybackState == PlaybackState.Stopped) 
            {
                _audioFile.Position = 0;
                _outputDevice.Play();
            }
        }
    }
}

