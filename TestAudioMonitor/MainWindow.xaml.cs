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
    private static string? pathToConfigFile;
    private static string? pathToUWFile;
    private static Dictionary<int, string>? songPaths;
    private static FileSystemWatcher? watcher;
    private const string pathToPreviousConfig = "previous_settings.txt";
    private static WaveOutEvent? outputDevice;
    private static AudioFileReader? audioFile;
    private static int previousSongID = -1;
    private static bool stopPlaying = false;
    public MainWindow()
    {
        InitializeComponent();
        if (Path.Exists(pathToPreviousConfig))
        {
            var content = File.ReadAllLines(pathToPreviousConfig);
            Debug.Assert(content.Length == 2);
            pathToConfigFile = content[0];
            pathToUWFile = content[1];
            if (Path.Exists(pathToConfigFile) & (Path.Exists(pathToUWFile)))
            {
                EventsList.Items.Add($"Loaded previous settings from {Path.GetFullPath(pathToPreviousConfig)}");
                EventsList.Items.Add($"Loaded song config file from {Path.GetFullPath(pathToConfigFile)}");
                EventsList.Items.Add($"Loaded UW file from {Path.GetFullPath(pathToUWFile)}");
                LoadJsonIntoDict(pathToConfigFile);
            }
            else
            {
                EventsList.Items.Add($"Tried loading previous settings from {Path.GetFullPath(pathToPreviousConfig)} but some of the files referenced couldn't be found");
            }
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if ((pathToUWFile is null) | (pathToUWFile == ""))
        {
            MessageBox.Show("Please specify a path to the UW file");
            return;
        }
        if ((pathToConfigFile is null) | (pathToConfigFile == ""))
        {
            MessageBox.Show("Please specify a path to the config file");
            return;
        }
        if (songPaths is null)
        {
            MessageBox.Show("Song Paths weren't loaded properly!");
            return;
        }
        watcher = new FileSystemWatcher(Path.GetDirectoryName(pathToUWFile), Path.GetFileName(pathToUWFile));
        watcher.NotifyFilter = NotifyFilters.LastWrite;

        // Subscribe to the Changed event
        watcher.Changed += OnFileChanged;

        // Start watching
        watcher.EnableRaisingEvents = true;

        // Saves settings
        var writer = File.CreateText(pathToPreviousConfig);
        writer.Write(pathToConfigFile);
        writer.Write('\n');
        writer.Write(pathToUWFile);
        writer.Flush();
        writer.Close();
        stopPlaying = false;
    }

    private void UWFile_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = "Select the path to the UW file",
            Filter = "All Files (*.*)|*.*", // You can customize the file filter if needed
            CheckFileExists = true,
            CheckPathExists = true
        };

        // Show the dialog and get the result
        bool? result = openFileDialog.ShowDialog();
        if (result == true)
        {
            pathToUWFile = openFileDialog.FileName;
            EventsList.Items.Add($"UW file loaded from {Path.GetFullPath(pathToUWFile)}");
        }
        else
        {
            MessageBox.Show("Failed to open file");
        }
    }

    private void ConfigFile_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = "Select the path to the config file",
            Filter = "All Files (*.*)|*.*", // You can customize the file filter if needed
            CheckFileExists = true,
            CheckPathExists = true
        };

        // Show the dialog and get the result
        bool? result = openFileDialog.ShowDialog();
        if (result == true)
        {
            pathToConfigFile = openFileDialog.FileName;
            EventsList.Items.Add($"Song config file loaded from {Path.GetFullPath(pathToConfigFile)}");
        }
        else
        {
            MessageBox.Show("Failed to open file");
            return;
        }
        LoadJsonIntoDict(pathToConfigFile);
    }

    private void LoadJsonIntoDict(string pathToConfigFile)
    {
        string jsonString = File.ReadAllText(pathToConfigFile);
        try
        {
            songPaths = JsonSerializer.Deserialize<Dictionary<int, string>>(jsonString);
        }
        catch (JsonException)
        {
            MessageBox.Show("Failed to load JSON content");
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }
        if (songPaths is null)
        {
            MessageBox.Show("Failed to load JSON content (2)");
            return;
        }

        foreach (var p in songPaths)
        {
            if (!File.Exists(p.Value))
            {
                MessageBox.Show($"Could not find song at path {p.Value}. Please fix the JSON file again");
                songPaths = null;
                return;
            }
            EventsList.Items.Add($"Song number {p.Key} bound to {p.Value}");
        }

    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        outputDevice?.Stop();
        CurrentSongLabel.Content = string.Empty;
        stopPlaying = true;
        // StopPlayback();
        // Required?
    }


    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (songPaths is null)
        {
            MessageBox.Show("Song paths dict is null... Bug");
            return;
        }

        string content = File.ReadAllText(e.FullPath);
        int.TryParse(content, out int trackNumber);

        // Don't do anything if somehow the same value was present.
        if (previousSongID == trackNumber)
        {
            return;
        }

        var songPath = songPaths.GetValueOrDefault(trackNumber);
        if (string.IsNullOrEmpty(songPath))
        {
            EventsList.Items.Add($"Couldn't play song for track number {trackNumber}");
            return;
        }

        if (outputDevice is null)
        {
            outputDevice = new WaveOutEvent();
        }

        audioFile = new AudioFileReader(songPath);
        outputDevice.Init(audioFile);
        outputDevice.Play();
        CurrentSongLabel.Content = $"{trackNumber} {Path.GetFileNameWithoutExtension(songPath)}";
        outputDevice.PlaybackStopped += ResetSong;

        // John.Dispatcher.InvokeAsync(ResetSong, System.Windows.Threading.DispatcherPriority.SystemIdle);
    }
//    private void StopPlayback()
//    {
//        if (outputDevice is not null)
//        {
//            outputDevice.Dispose();
//            outputDevice = null;
//        }
//        if (audioFile is not null)
//        {
//            audioFile.Dispose();
//            audioFile = null;
//        }
//    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        // TODO: duplicate code. Fix
        if ((songPaths is not null) & (pathToUWFile is not null))
        {
            string content = File.ReadAllText(pathToUWFile);
            int.TryParse(content, out int trackNumber);


            var songPath = songPaths.GetValueOrDefault(trackNumber);
            if (string.IsNullOrEmpty(songPath))
            {
                return;
            }

            if (outputDevice is null)
            {
                outputDevice = new WaveOutEvent();
            }

            audioFile = new AudioFileReader(songPath);
            outputDevice.Init(audioFile);
            outputDevice.Play();
            CurrentSongLabel.Content = $"{trackNumber} {Path.GetFileNameWithoutExtension(songPath)}";
            outputDevice.PlaybackStopped += ResetSong;
            stopPlaying = false;

            // John.Dispatcher.InvokeAsync(ResetSong, System.Windows.Threading.DispatcherPriority.SystemIdle);
        }
    }

    private void ResetSong(object? sender, StoppedEventArgs e)
    {
        if (stopPlaying)
        {
            return;
        }
        if ((outputDevice is not null) & (audioFile is not null))
        {
            if (outputDevice.PlaybackState == PlaybackState.Stopped) 
            {
                audioFile.Position = 0;
                outputDevice.Play();
            }
        }
        // John.Dispatcher.InvokeAsync(ResetSong, System.Windows.Threading.DispatcherPriority.SystemIdle);
    }
}

