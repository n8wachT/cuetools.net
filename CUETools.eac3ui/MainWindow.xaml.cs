﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
//using System.Windows.Shapes;
using CUETools.Codecs.BDLPCM;
using CUETools.CDImage;
using CUETools.CTDB;
using System.ComponentModel;
using Krystalware.UploadHelper;
using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
//using Microsoft.Win32;

namespace CUETools.eac3ui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            textBoxSource.Text = Properties.Settings.Default.SourceFolder;
            textBoxDestination.Text = Properties.Settings.Default.DestinationFolder;
        }

        private void ButtonBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.SelectedPath = textBoxSource.Text;
                dialog.ShowNewFolderButton = false;
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                    textBoxSource.Text = dialog.SelectedPath;
            }
            //OpenFileDialog openFileDialog = new OpenFileDialog();
            //if (openFileDialog.ShowDialog() == true)
            //    textBoxSource.Text = openFileDialog.FileName;
        }

        private void buttonBrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.SelectedPath = textBoxDestination.Text;
                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                    textBoxDestination.Text = dialog.SelectedPath;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Properties.Settings.Default.SourceFolder = textBoxSource.Text;
            Properties.Settings.Default.DestinationFolder = textBoxDestination.Text;
            Properties.Settings.Default.Save();
        }

        private void textBoxSource_TextChanged(object sender, TextChangedEventArgs e)
        {
            var titleSets = new List<MPLSReader>();
            IEnumerable<string> playlists = null;
            try
            {
                playlists = Directory.EnumerateFiles(Path.Combine(Path.Combine(textBoxSource.Text, "BDMV"), "PLAYLIST"));
            }
            catch
            {
            }
            if (playlists != null)
                foreach (var playlist in playlists)
                {
                    titleSets.Add(new MPLSReader(playlist, null));
                }
            cmbTitleSet.ItemsSource = titleSets;
            cmbTitleSet.SelectedIndex = 0;
        }

        private void cmbTitleSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            cmbMetadata.ItemsSource = new List<CTDBResponseMeta>();
            ctdb = null;

            var audios = new List<MPLSStream>();
            if (e.AddedItems.Count == 1)
            {
                MPLSReader rdr = e.AddedItems[0] as MPLSReader;
                rdr.MPLSHeader.play_item.ForEach(i => i.audio.ForEach(v => { if (!audios.Exists(v1 => v1.pid == v.pid)) audios.Add(v); }));

                var chapters = rdr.Chapters;
                if (chapters.Count > 2)
                {
                    string strtoc = "";
                    for (int i = 0; i < chapters.Count; i++)
                        strtoc += string.Format(" {0}", chapters[i] / 600);
                    strtoc = strtoc.Substring(1);
                    CDImageLayout toc = new CDImageLayout(strtoc);
                    ctdb = new CUEToolsDB(toc, null);
                    workerCtdb = new BackgroundWorker();
                    workerCtdb.DoWork += workerCtdb_DoWork;
                    workerCtdb.RunWorkerAsync();
                }
            }
            cmbAudioTrack.ItemsSource = audios;
            cmbAudioTrack.SelectedIndex = 0;
        }

        void workerCtdb_DoWork(object sender, DoWorkEventArgs e)
        {
            //Console.Error.WriteLine("Contacting CTDB...");
            this.Dispatcher.Invoke(() =>
            {
                pbStatus.Visibility = Visibility.Visible;
                pbStatus.IsIndeterminate = true;
            });
            //ctdb.UploadHelper.onProgress += worker_ctdbProgress;
            ctdb.ContactDB(null, "CUETools.eac3to 2.1.7", "", false, true, CTDBMetadataSearch.Extensive);
            this.Dispatcher.Invoke(() =>
            {
                pbStatus.Visibility = Visibility.Collapsed;
                pbStatus.IsIndeterminate = false;
                cmbMetadata.ItemsSource = ctdb.Metadata;
                cmbMetadata.SelectedIndex = 0;
            });
        }

        //private void worker_ctdbProgress(object sender, UploadProgressEventArgs args)
        //{
        //    this.Dispatcher.Invoke(() =>
        //    {
        //        pbStatus.Value = (int)args.percent;
        //    });
        //}

        private void cmbAudioTrack_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 1)
            {
                MPLSStream stream = (MPLSStream)(e.AddedItems[0]);
            }
        }

        BackgroundWorker workerCtdb;
        CUEToolsDB ctdb;

        BackgroundWorker workerExtract;
        CTDBResponseMeta meta;
        MPLSReader chosenReader;
        ushort pid;
        string outputFolderPath;
        string outputAudioPath;
        string outputCuePath;

        private void buttonExtract_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTitleSet.SelectedItem == null) return;
            pid = ((MPLSStream)cmbAudioTrack.SelectedItem).pid;
            chosenReader = cmbTitleSet.SelectedItem as MPLSReader;
            meta = cmbMetadata.SelectedItem as CTDBResponseMeta;
            outputFolderPath = Path.Combine(textBoxDestination.Text, meta != null ? 
                meta.artist + " - " + meta.year + " - " + meta.album :
                Path.GetFileName(textBoxSource.Text) + "." + chosenReader.FileName + "." + pid.ToString());
            outputAudioPath = Path.Combine(outputFolderPath, meta != null ? meta.artist + " - " + meta.year + " - " + meta.album + ".flac" : "image.flac");
            outputCuePath = Path.ChangeExtension(outputAudioPath, "cue");

            pbStatus.Visibility = Visibility.Visible;
            pbStatus.Value = 0.0;
            //pbStatus.IsIndeterminate = true;
            stackParams.IsEnabled = false;
            buttonExtract.IsEnabled = false;
            buttonStop.Visibility = Visibility.Visible;
            buttonStop.IsEnabled = true;

            workerExtract = new BackgroundWorker();
            workerExtract.WorkerSupportsCancellation = true;
            workerExtract.DoWork += workerExtract_DoWork;
            workerExtract.RunWorkerAsync();
        }

        void workerExtract_DoWork(object sender, DoWorkEventArgs e)
        {
            MPLSReader reader = null;
            try
            {
                reader = new MPLSReader(chosenReader.Path, null, pid);
                Directory.CreateDirectory(outputFolderPath);
                if (File.Exists(outputCuePath)) throw new Exception(string.Format("File \"{0}\" already exists", outputCuePath));
                if (File.Exists(outputAudioPath)) throw new Exception(string.Format("File \"{0}\" already exists", outputAudioPath));
                AudioBuffer buff = new AudioBuffer(reader, 0x10000);
                FlakeWriterSettings settings = new FlakeWriterSettings()
                {
                    PCM = reader.PCM,
                    Padding = 16536,
                    EncoderMode = "5"
                };
                if (ctdb != null)
                {
                    using (StreamWriter cueWriter = new StreamWriter(outputCuePath, false, Encoding.UTF8))
                    {
                        cueWriter.WriteLine("REM COMMENT \"{0}\"", "Created by CUETools.eac3to");
                        if (meta != null && meta.year != null)
                            cueWriter.WriteLine("REM DATE {0}", meta.year);
                        else
                            cueWriter.WriteLine("REM DATE XXXX");
                        if (meta != null)
                        {
                            cueWriter.WriteLine("PERFORMER \"{0}\"", meta.artist);
                            cueWriter.WriteLine("TITLE \"{0}\"", meta.album);
                        }
                        else
                        {
                            cueWriter.WriteLine("PERFORMER \"\"");
                            cueWriter.WriteLine("TITLE \"\"");
                        }
                        cueWriter.WriteLine("FILE \"{0}\" WAVE", Path.GetFileName(outputAudioPath));
                        var toc = ctdb.TOC;
                        for (int track = 1; track <= toc.TrackCount; track++)
                            if (toc[track].IsAudio)
                            {
                                cueWriter.WriteLine("  TRACK {0:00} AUDIO", toc[track].Number);
                                if (meta != null && meta.track.Length >= toc[track].Number)
                                {
                                    cueWriter.WriteLine("    TITLE \"{0}\"", meta.track[(int)toc[track].Number - 1].name);
                                    if (meta.track[(int)toc[track].Number - 1].artist != null)
                                        cueWriter.WriteLine("    PERFORMER \"{0}\"", meta.track[(int)toc[track].Number - 1].artist);
                                }
                                else
                                {
                                    cueWriter.WriteLine("    TITLE \"\"");
                                }
                                if (toc[track].ISRC != null)
                                    cueWriter.WriteLine("    ISRC {0}", toc[track].ISRC);
                                for (int index = toc[track].Pregap > 0 ? 0 : 1; index <= toc[track].LastIndex; index++)
                                    cueWriter.WriteLine("    INDEX {0:00} {1}", index, toc[track][index].MSF);
                            }
                    }
                }
                var start = DateTime.Now;
                TimeSpan lastPrint = TimeSpan.FromMilliseconds(0);
                var writer = new FlakeWriter(outputAudioPath, settings);
                try
                {
                    while (reader.Read(buff, -1) != 0)
                    {
                        writer.Write(buff);
                        TimeSpan elapsed = DateTime.Now - start;
                        if ((elapsed - lastPrint).TotalMilliseconds > 60)
                        {
                            long length = Math.Max((long)(reader.Duration.TotalSeconds * reader.PCM.SampleRate), Math.Max(reader.Position, 1));
                            this.Dispatcher.Invoke(() =>
                            {
                                pbStatus.Value = 100.0 * reader.Position / length;
                            });
                            lastPrint = elapsed;
                        }
                        if (workerExtract.CancellationPending)
                        {
                            throw new Exception("aborted");
                        }
                    }
                }
                catch (Exception ex)
                {
                    writer.Delete();
                    try { File.Delete(outputCuePath); } catch (Exception) { }
                    throw ex;
                }
                writer.Close();
            }
            catch (Exception ex)
            {
                this.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(this, ex.Message, "Extraction failed");
                });
            }
            finally
            {
                if (reader != null) reader.Close();
            }

            this.Dispatcher.Invoke(() =>
            {
                pbStatus.Visibility = Visibility.Collapsed;
                //pbStatus.IsIndeterminate = false;
                stackParams.IsEnabled = true;
                buttonExtract.IsEnabled = true;
                buttonStop.Visibility = Visibility.Hidden;
                buttonStop.IsEnabled = false;
            });
        }

        private void buttonStop_Click(object sender, RoutedEventArgs e)
        {
            workerExtract.CancelAsync();
            buttonStop.Visibility = Visibility.Hidden;
            buttonStop.IsEnabled = false;
        }
    }
}