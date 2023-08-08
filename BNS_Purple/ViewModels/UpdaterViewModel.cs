using BNS_Purple.Extensions;
using BNS_Purple.Functions;
using BNS_Purple.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using static BNS_Purple.Models.NCService;

namespace BNS_Purple.ViewModels
{
    public partial class UpdaterViewModel : ObservableObject
    {
        private readonly NCService _ncService;
        private readonly AppData _appData;
        private readonly httpClient _httpClient;
        private uint localBuild, onlineBuild = 0;
        private string BASE_URL = NCService.ERegions.KR.GetAttribute<CDNAttribute>().Name; // Hard coded for now until more regions added
        private string BNS_PATH = string.Empty;
        private long currentBytes = 0L;
        private long totalBytes = 0L;
        private BackgroundWorker _worker = new BackgroundWorker();
        private BackgroundWorker _downloadWorker = new BackgroundWorker();
        private NetworkPerformanceReporter Network = null;
        private DispatcherTimer downloadTimer;
        private List<string> _errorLog = new List<string>();

        [ObservableProperty]
        private string localBuildLabel;

        [ObservableProperty]
        private string onlineBuildLabel;

        [ObservableProperty]
        private bool isCustomPatch = false;

        [ObservableProperty]
        private Brush onlineLabelColor = Brushes.AliceBlue;

        [ObservableProperty]
        private Brush localLabelColor = Brushes.AliceBlue;

        [ObservableProperty]
        private double updateProgressValue = 0.00;

        [ObservableProperty]
        private Visibility patchingInProgress = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility customPatchIsVisible = Visibility.Visible;

        [ObservableProperty]
        private string customPatchBuild;

        [ObservableProperty]
        private string actionButtonText = "File Check";

        [ObservableProperty]
        private string patchingLabel = "";

        [ObservableProperty]
        private string progressBlock = "";

        public UpdaterViewModel (NCService ncService, AppData appData, httpClient httpClient)
        {
            _ncService = ncService;
            _appData = appData;
            _httpClient = httpClient;
            _worker.DoWork += new DoWorkEventHandler(PatchGameWorker);
            _worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(PatchGameCompleted);
            ServicePointManager.DefaultConnectionLimit = 50;

            _downloadWorker.DoWork += new DoWorkEventHandler(DownloadTimer_Tick);
            _downloadWorker.WorkerSupportsCancellation = true;
        }

        private void PatchGameCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            UpdateProgressValue = 0.00;
            ActionButtonText = "File Check";
            PatchingLabel = "";
            ProgressBlock = "";
            PatchingInProgress = Visibility.Collapsed;
            CustomPatchIsVisible = Visibility.Visible;

            if (IsCustomPatch)
                Initialize();
            else
                LocalLabelColor = Brushes.AliceBlue;
        }

        private void DownloadTimer_Tick(object? sender, DoWorkEventArgs e)
        {
            while (!(sender as BackgroundWorker).CancellationPending)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ProgressBlock = string.Format("Download Speed: {0}/s ", IO.SizeSuffix(Network.GetNetworkPerformanceData().BytesReceived, 2));
                }));
                Thread.Sleep(1000);
            }
            e.Cancel = true;
        }

        public void UpdateView()
        {
            if (!_worker.IsBusy)
                Initialize();
        }

        public void Initialize()
        {
            var buildInfo = _ncService.GetVersionInfoRelease(NCService.ERegions.KR);
            if (buildInfo != null)
            {
                OnlineLabelColor = Brushes.AliceBlue;
                onlineBuild = buildInfo.GlobalVersion; // Override this for testing purposes (self note)
                OnlineBuildLabel = onlineBuild.ToString();
            } else
            {
                OnlineLabelColor = Brushes.Red;
                OnlineBuildLabel = "Error";
            }

            // Precaution for if the repo address changes in the future we retrieve it from NCService
            try
            {
                var repo = _ncService.GetGameInfoUpdateRequest(NCService.ERegions.KR);
                if (repo != null)
                    BASE_URL = repo.RepositoryServerAddress;
            } catch
            {
                // Log Later?
            }

            // Local stuff
            string versionInfo_path = Path.Combine(_appData.GamePath, $"VersionInfo_{NCService.ERegions.KR.GetAttribute<GameIdAttribute>().Name}.xml");
            if (File.Exists(versionInfo_path))
            {
                XDocument versionInfo = XDocument.Load(versionInfo_path);
                var localInfo = versionInfo.XPathSelectElement("VersionInfo/Version");
                if (localInfo != null)
                {
                    localBuild = uint.Parse(localInfo.Value);
                    LocalBuildLabel = localInfo.Value;

                    if (LocalBuildLabel != OnlineBuildLabel)
                    {
                        LocalLabelColor = Brushes.Red;
                        ActionButtonText = "Update";
                    }
                }
            } else
            {
                LocalLabelColor = Brushes.Red;
                localBuild = 0;
                LocalBuildLabel = "Error";
                ActionButtonText = "Install";
            }
        }

        [RelayCommand]
        void PatchGame()
        {
            if (IsCustomPatch)
                onlineBuild = (uint)int.Parse(CustomPatchBuild);

            _worker.RunWorkerAsync();
            PatchingInProgress = Visibility.Visible;
            CustomPatchIsVisible = Visibility.Collapsed;
        }

        public struct PatchFile_FlagType
        {
            public const string Unknown = "0";
            public const string UnChanged = "1";
            public const string Changed = "2";
            public const string ChangedDiff = "3";
            public const string ChangedOriginal = "4";
            public const string Added = "5";
        }

        public class UPDATE_FILE_ENTRY
        {
            public PURPLE_FILES_STRUCT fileInfo { get; set; }
            public bool Downloaded { get; set; }
        }

        private void PatchGameWorker(object sender, DoWorkEventArgs e)
        {
            try
            {
                BNS_PATH = Path.GetFullPath(_appData.GamePath);

                string fileInfo_name = "files_info.json";
                string targetVersion = "0";

            StartPatchThread:
                Application.Current.Dispatcher.BeginInvoke(new Action(() => { UpdateProgressValue = 0.00; }));
                if (localBuild != 0 && !IsCustomPatch)
                {
                    if (onlineBuild - localBuild > 1)
                        targetVersion = (localBuild + 1).ToString();
                    else
                        targetVersion = onlineBuild.ToString();
                }
                else
                    targetVersion = onlineBuild.ToString();

                string FileInfo_URL = string.Format(@"http://{0}/{1}/{2}/Patch/{3}.zip", BASE_URL, NCService.ERegions.KR.GetAttribute<GameIdAttribute>().Name, targetVersion, fileInfo_name);

                totalBytes = 0L;
                currentBytes = 0L;
                _errorLog = new List<string>();

                List<UPDATE_FILE_ENTRY> update_file_map = new List<UPDATE_FILE_ENTRY>();

                int i_targetVersion = int.Parse(targetVersion);
                int i_localVersion = (int)localBuild;
                bool deltaPatch = localBuild != 0 && (i_targetVersion - i_localVersion) == 1;
                string PatchDirectory = Path.Combine(BNS_PATH, "PatchManager", targetVersion);

                if (!_httpClient.RemoteFileExists(FileInfo_URL))
                    throw new Exception(String.Format("files_info.json for build #{0} could not be reached", targetVersion));

                if (!Directory.Exists(PatchDirectory))
                    Directory.CreateDirectory(PatchDirectory);

                Application.Current.Dispatcher.BeginInvoke(new Action(() => { ProgressBlock = $"Retreiving {fileInfo_name}"; }));
                if (!_httpClient.Download(FileInfo_URL, Path.Combine(PatchDirectory, fileInfo_name + ".zip"), false))
                    throw new Exception("Failed to download " + fileInfo_name);

                Application.Current.Dispatcher.BeginInvoke(new Action(() => { ProgressBlock = $"Decompressing {fileInfo_name}"; }));
                IO.DecompressFileLZMA(Path.Combine(PatchDirectory, fileInfo_name + ".zip"), Path.Combine(PatchDirectory, fileInfo_name));

                PURPLE_FILE_INFO file_info = JsonConvert.DeserializeObject<PURPLE_FILE_INFO>(File.ReadAllText(Path.Combine(PatchDirectory, fileInfo_name)));
                if (file_info == null)
                    throw new Exception("Failed to parse files_info.json");

                int totalFiles = file_info.files.Count;
                int processedFiles = 0;

                Application.Current.Dispatcher.BeginInvoke(new Action(() => { PatchingLabel = "Scanning"; }));

                Parallel.ForEach<PURPLE_FILES_STRUCT>(file_info.files, new ParallelOptions { MaxDegreeOfParallelism = _appData.UpdaterThreads + 1 }, delegate (PURPLE_FILES_STRUCT fileData)
                {
                    if (fileData.patchType == PatchFile_FlagType.Unknown) return;

                    FileInfo fileInfo = new FileInfo(Path.Combine(BNS_PATH, fileData.path));

                    if (_appData.IgnoreHashCheck && deltaPatch)
                    {
                        if (fileData.patchType == PatchFile_FlagType.Added || fileData.patchType == PatchFile_FlagType.ChangedOriginal)
                        {
                            if (fileData.patchType == PatchFile_FlagType.Added)
                                Interlocked.Add(ref totalBytes, long.Parse(fileData.encodedInfo.size));
                            else
                                Interlocked.Add(ref totalBytes, long.Parse(fileData.deltaInfo.size));

                            update_file_map.Add(new UPDATE_FILE_ENTRY { fileInfo = fileData, Downloaded = false });
                        }
                    }
                    else
                    {
                        string fHash = fileInfo.Exists ? Crypto.SHA1_File(fileInfo.FullName) : "";
                        if (deltaPatch)
                        {
                            if (fileInfo.Exists && fHash == fileData.hash) goto FileInfoEnd;
                            if (!fileInfo.Exists)
                                Interlocked.Add(ref totalBytes, long.Parse(fileData.encodedInfo.size));
                            else
                            {
                                if (fileData.patchType != PatchFile_FlagType.ChangedOriginal)
                                    Interlocked.Add(ref totalBytes, long.Parse(fileData.encodedInfo.size));
                                else
                                    Interlocked.Add(ref totalBytes, long.Parse(fileData.deltaInfo.size));
                            }

                            update_file_map.Add(new UPDATE_FILE_ENTRY { fileInfo = fileData, Downloaded = false });
                        }
                        else
                        {
                            if (fileInfo.Exists && fHash == fileData.hash) goto FileInfoEnd;

                            Interlocked.Add(ref totalBytes, long.Parse(fileData.encodedInfo.size));
                            update_file_map.Add(new UPDATE_FILE_ENTRY { fileInfo = fileData, Downloaded = false });
                        }
                    }

                FileInfoEnd:
                    Interlocked.Increment(ref processedFiles);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProgressValue = (int)((double)processedFiles / totalFiles * 100);
                        ProgressBlock = $"{processedFiles} / {totalFiles} files scanned";
                    }));
                });

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ProgressBlock = $"Download Size: {IO.SizeSuffix(totalBytes, 2)} ({update_file_map.Count}) files";
                }));

                totalFiles = update_file_map.Count();
                if (totalFiles <= 0) goto Cleanup;
                //update_file_map.ForEach(x => Debug.WriteLine(x.fileInfo.path));

                processedFiles = 0;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateProgressValue = 0.00;
                    PatchingLabel = "Downloading...";
                }));

                Thread.Sleep(1000);

                // Network counter for this process
                Network = NetworkPerformanceReporter.Create();
                _downloadWorker.RunWorkerAsync();

                Parallel.ForEach<UPDATE_FILE_ENTRY>(update_file_map, new ParallelOptions { MaxDegreeOfParallelism = _appData.DownloadThreads + 1 }, delegate (UPDATE_FILE_ENTRY file)
                {
                    if (file == null)
                        return;

                    if (!Directory.Exists(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path))))
                        Directory.CreateDirectory(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path)));

                    try
                    {
                        if (file.fileInfo.patchType == PatchFile_FlagType.Added)
                        {
                            if (file.fileInfo.encodedInfo.separates == null)
                            {
                                if (!_httpClient.Download(
                                    string.Format(@"http://{0}/{1}", BASE_URL, file.fileInfo.encodedInfo.path),
                                    Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.encodedInfo.path)),
                                    true, file.fileInfo.encodedInfo.hash))
                                {
                                    _errorLog.Add($"{Path.GetFileName(file.fileInfo.path)} failed to download");
                                    goto EndOfThread;
                                }
                            } else
                            {
                                foreach (var f in file.fileInfo.encodedInfo.separates)
                                {
                                    if (!_httpClient.Download(string.Format(@"http://{0}/{1}", BASE_URL, f.path), Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(f.path)), true, f.hash))
                                    {
                                        _errorLog.Add($"{Path.GetFileName(file.fileInfo.path)} failed to download");
                                        goto EndOfThread;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Check if we can delta patch, If we can delta patch then it may be a failed hash check.
                            if (!deltaPatch || (File.Exists(Path.Combine(BNS_PATH, file.fileInfo.path)) && file.fileInfo.patchType != PatchFile_FlagType.ChangedOriginal))
                            {
                                if (file.fileInfo.encodedInfo.separates == null)
                                {
                                    if (!_httpClient.Download(
                                    string.Format(@"http://{0}/{1}", BASE_URL, file.fileInfo.encodedInfo.path),
                                    Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.encodedInfo.path)), true, file.fileInfo.encodedInfo.hash))
                                    {
                                        _errorLog.Add($"{Path.GetFileName(file.fileInfo.path)} failed to download");
                                        goto EndOfThread;
                                    }
                                }
                                else
                                {
                                    foreach (var f in file.fileInfo.encodedInfo.separates)
                                    {
                                        if(!_httpClient.Download(string.Format(@"http://{0}/{1}", BASE_URL, f.path), Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(f.path)), true, f.hash))
                                        {
                                            _errorLog.Add($"{Path.GetFileName(file.fileInfo.path)} failed to download");
                                            goto EndOfThread;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Delta patching part
                                PURPLE_ENCODED_INFO? files;
                                if (deltaPatch)
                                    files = file.fileInfo.deltaInfo ?? file.fileInfo.encodedInfo;
                                else
                                    files = file.fileInfo.encodedInfo;

                                if (files.separates == null)
                                {
                                    if (!_httpClient.Download(
                                        string.Format(@"http://{0}/{1}", BASE_URL, files.path),
                                        Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(files.path)), true, files.hash))
                                    {
                                        _errorLog.Add($"{Path.GetFileName(file.fileInfo.path)} failed to download");
                                        goto EndOfThread;
                                    }
                                }
                                else
                                {
                                    foreach (var f in files.separates)
                                    {
                                        if(!_httpClient.Download(string.Format(@"http://{0}/{1}", BASE_URL, f.path), Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(f.path)), true, f.hash))
                                        {
                                            _errorLog.Add($"{Path.GetFileName(file.fileInfo.path)} failed to download");
                                            goto EndOfThread;
                                        }
                                    }
                                }
                            }
                        }

                        file.Downloaded = true;
                        Interlocked.Increment(ref processedFiles);

                        if (deltaPatch && file.fileInfo.patchType == PatchFile_FlagType.ChangedOriginal)
                            Interlocked.Add(ref currentBytes, long.Parse(file.fileInfo.deltaInfo.size ?? file.fileInfo.encodedInfo.size));
                        else
                            Interlocked.Add(ref currentBytes, long.Parse(file.fileInfo.encodedInfo.size));
                    }
                    catch (Exception ex)
                    {
                        _errorLog.Add(ex.Message);
                       // Debug.WriteLine(ex);
                        //Logger.log.Error("GameUpdater: {0}", ex.Message);
                    }

                EndOfThread:
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProgressValue = (int)((double)processedFiles / totalFiles * 100);
                        PatchingLabel = String.Format("{0} / {1}", IO.SizeSuffix(currentBytes, 2), IO.SizeSuffix(totalBytes, 2));
                    }));
                });

                _downloadWorker.CancelAsync();
                Network.Dispose();
                Thread.Sleep(2000);

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateProgressValue = 0.00;
                    PatchingLabel = "Patching";
                    ProgressBlock = "";
                }));

                Thread.Sleep(1500);
                processedFiles = 0;

                Parallel.ForEach<UPDATE_FILE_ENTRY>(update_file_map, new ParallelOptions { MaxDegreeOfParallelism = _appData.UpdaterThreads + 1 }, delegate (UPDATE_FILE_ENTRY file)
                {
                    try
                    {
                        string destination = Path.GetFullPath(Path.Combine(BNS_PATH, Path.GetDirectoryName(file.fileInfo.path)));
                        if (deltaPatch && file.fileInfo.patchType == PatchFile_FlagType.ChangedOriginal)
                        {
                            if (file.fileInfo.deltaInfo.separates != null)
                            {
                                List<string> archives = new List<string>();
                                file.fileInfo.deltaInfo.separates.ForEach(e => archives.Add(Path.GetFileName(e.path)));
                                var result = IO.DecompressStreamLZMA(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path)), archives, $"{Path.GetFileName(file.fileInfo.path)}.dlt");
                            } else
                            {
                                IO.DecompressFileLZMA(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.deltaInfo.path)), Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), $"{Path.GetFileName(file.fileInfo.path)}.dlt"));
                            }

                            // Delta is unpacked
                            if (IO.DeltaPatch(Path.Combine(destination, Path.GetFileName(file.fileInfo.path)), Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), $"{Path.GetFileName(file.fileInfo.path)}.dlt")))
                            {
                                // this is where i'll move stuff?
                                File.Delete(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), $"{Path.GetFileName(file.fileInfo.path)}.dlt"));

                                if (File.Exists(Path.Combine(destination, Path.GetFileName(file.fileInfo.path))))
                                    File.Delete(Path.Combine(destination, Path.GetFileName(file.fileInfo.path)));

                                File.Move(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.path)), Path.Combine(destination, Path.GetFileName(file.fileInfo.path)));
                            }
                            else
                                throw new Exception($"{Path.GetFileName(file.fileInfo.path)} failed to delta patch");
                        } else
                        {
                            if (file.fileInfo.encodedInfo.separates != null)
                            {
                                List<string> archives = new List<string>();
                                file.fileInfo.encodedInfo.separates.ForEach(e => archives.Add(Path.GetFileName(e.path)));
                                var result = IO.DecompressStreamLZMA(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path)), archives, Path.GetFileName(file.fileInfo.path), false);
                            } else
                            {
                                IO.DecompressFileLZMA(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.encodedInfo.path)), Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.path)));
                            }

                            // File is unpacked, this is where i'll move stuff?
                            File.Move(Path.Combine(PatchDirectory, Path.GetDirectoryName(file.fileInfo.path), Path.GetFileName(file.fileInfo.path)), Path.Combine(destination, Path.GetFileName(file.fileInfo.path)), true);
                        }
                    } catch (Exception ex)
                    {
                        _errorLog.Add(ex.Message);
                        //Debug.WriteLine(ex);
                    } finally
                    {
                        Interlocked.Increment(ref processedFiles);
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            UpdateProgressValue = (int)((double)processedFiles / totalFiles * 100);
                            ProgressBlock = $"{IO.SizeSuffix(totalBytes, 2)} ({UpdateProgressValue}%)";
                        }));
                    }
                });

                Cleanup:
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => { ProgressBlock = "Internal Check"; }));
                    if (totalFiles > 0 && update_file_map.Any(x => !x.Downloaded))
                        _errorLog.Add("Download checks failed");

                    Thread.Sleep(500);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => { ProgressBlock = "Cleaning up"; }));

                    if (_errorLog.Count == 0)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => { LocalBuildLabel = targetVersion; }));
                        localBuild = (uint)int.Parse(targetVersion);
                        Directory.Delete(PatchDirectory, true);

                        // Update xml
                        string versionInfo_path = Path.Combine(_appData.GamePath, $"VersionInfo_{NCService.ERegions.KR.GetAttribute<GameIdAttribute>().Name}.xml");
                        if (File.Exists(versionInfo_path))
                        {
                            XDocument versionInfo = XDocument.Load(versionInfo_path);
                            var localInfo = versionInfo.XPathSelectElement("VersionInfo/Version");
                            localInfo.Value = localBuild.ToString();
                            versionInfo.Save(versionInfo_path);
                        }
                        else
                        {
                            // It doesn't exist so we need to create it
                            using (XmlWriter writer = XmlWriter.Create(versionInfo_path))
                            {
                                writer.WriteStartElement("VersionInfo");
                                writer.WriteElementString("Version", localBuild.ToString());
                                writer.WriteElementString("LocalDownloadIndex", "0");
                                writer.WriteElementString("Updated", "1");
                                writer.WriteElementString("SelectedFolders", "");
                                writer.WriteEndElement();
                                writer.Flush();
                            }
                        }

                        if (targetVersion != onlineBuild.ToString())
                                goto StartPatchThread;
                    }
                    else
                        goto StartPatchThread;

                } catch (Exception ex)
            {
                //Debug.WriteLine(ex);
            }
        }
    }
}
