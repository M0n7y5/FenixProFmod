using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Fmod5Sharp.FmodTypes;
using Fmod5Sharp;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using System.IO.MemoryMappedFiles;
using DynamicData;
using System.Text;
using Microsoft.IO;
using Cysharp.Diagnostics;
using System.Reactive.Subjects;
using System.Reflection.PortableExecutable;

namespace FenixProFmodAva.ViewModels;

public class MainViewModel : ViewModelBase
{
    public Interaction<string, string> PickAFolder { get; } = new();

    public Interaction<string[], Unit> MsgBoxError { get; } = new();

    public Interaction<string[], Unit> MsgBoxInfo { get; } = new();

    public Interaction<string[], bool> MsgBoxYesNo { get; } = new();

    public Interaction<string, Unit> AddConsoleLine { get; } = new();


    [Reactive]
    public bool IsPathsReadOnly { get; set; } = false;

    [Reactive]
    public string BanksPath { get; set; } = string.Empty;

    [Reactive]
    public string WavsPath { get; set; } = string.Empty;

    [Reactive]
    public string BuildPath { get; set; } = string.Empty;

    public string FsbPath { get; set; } = string.Empty;

    [Reactive]
    public string ProgressText { get; set; } = "Idle";

    [Reactive]
    public int ProgressMaximum { get; set; } = 1;

    [Reactive]
    public int ProgressValue { get; set; } = 0;

    public Subject<string> ConsoleLine { get; } = new();

    public ReactiveCommand<Unit, Unit> PickBanksFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> PickWavsFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> PickBuildFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> ExtractWavsCommand { get; }
    public ReactiveCommand<Unit, Unit> RebuildBanksCommand { get; }

    private RecyclableMemoryStreamManager memoryStreamManager { get; } = new();

    public MainViewModel()
    {
        this.PickBanksFolderCommand = ReactiveCommand.CreateFromTask(PickBanksFolder);
        this.PickWavsFolderCommand = ReactiveCommand.CreateFromTask(PickWavsFolder);
        this.PickBuildFolderCommand = ReactiveCommand.CreateFromTask(PickBuildFolder);

        this.ExtractWavsCommand = ReactiveCommand.CreateFromTask(ExtractWavs);
        this.RebuildBanksCommand = ReactiveCommand.CreateFromTask(RebuildBanks);

        this.ExtractWavsCommand.ThrownExceptions.Subscribe(async ex =>
        {
            await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    $"Error: {ex.Message}" }).ToTask();
        });

        this.RebuildBanksCommand.ThrownExceptions.Subscribe(async ex =>
        {
            await MsgBoxError.Handle(new string[] {
                            "Something Bad Happened!",
                            $"Error: {ex.Message}" }).ToTask();
        });

        var curDir = Environment.CurrentDirectory;

        BanksPath = Path.Combine(curDir, "banks");
        WavsPath = Path.Combine(curDir, "wavs");
        BuildPath = Path.Combine(curDir, "build");
        FsbPath = Path.Combine(curDir, "fsb");

        if (Directory.Exists(BanksPath) == false)
            Directory.CreateDirectory(BanksPath);

        if (Directory.Exists(WavsPath) == false)
            Directory.CreateDirectory(WavsPath);

        if (Directory.Exists(BuildPath) == false)
            Directory.CreateDirectory(BuildPath);

        if (Directory.Exists(FsbPath) == false)
            Directory.CreateDirectory(FsbPath);

    }

    async Task PickBanksFolder()
    {
        var path = await PickAFolder.Handle("Select Banks Folder:").ToTask();

        if (path! == string.Empty)
            return;

        BanksPath = path;
    }

    async Task PickWavsFolder()
    {
        var path = await PickAFolder.Handle("Select Folder for extracted Wavs:").ToTask();

        if (path! == string.Empty)
            return;

        WavsPath = path;
    }

    async Task PickBuildFolder()
    {
        var path = await PickAFolder.Handle("Select Folder for rebuilded Banks:").ToTask();

        if (path! == string.Empty)
            return;

        BuildPath = path;
    }

    int Search(byte[] src, byte[] pattern)
    {
        int maxFirstCharSlot = src.Length - pattern.Length + 1;
        for (int i = 0; i < maxFirstCharSlot; i++)
        {
            if (src[i] != pattern[0]) // compare only first byte
                continue;

            // found a match on first byte, now try to match rest of the pattern
            for (int j = pattern.Length - 1; j >= 1; j--)
            {
                if (src[i + j] != pattern[j]) break;
                if (j == 1) return i;
            }
        }
        return -1;
    }

    async ValueTask<bool> ExtractFSB(string filename, string outputFileName)
    {
        try
        {
            File.Delete(outputFileName);
        }
        catch (Exception)
        {
            // i dont care now
        }

        using var file = File.OpenRead(filename);
        using var memory = memoryStreamManager.GetStream();
        using var reader = new BinaryReader(memory);

        await file.CopyToAsync(memory);

        memory.Position = 0;

        var headerIndex = Search(memory.GetBuffer(), Encoding.ASCII.GetBytes("SNDH"));

        memory.Position = 0;

        if (headerIndex == 0)
            return false;

        using var fsbFile = File.OpenWrite(outputFileName);

        memory.Position = headerIndex + 12;
        var nextOffset = reader.ReadInt32();
        reader.ReadInt32();
        memory.Position = nextOffset;

        await memory.CopyToAsync(fsbFile);

        await file.FlushAsync();
        await memory.FlushAsync();
        await fsbFile.FlushAsync();

        return true;
    }

    async Task ExtractWavs()
    {
        IsPathsReadOnly = true;

        var proceed = await MsgBoxYesNo.Handle(new string[] {
                    "Warning!",
                    "Following operation will overwrite existing WAV files! Do you want to proceed?" }).ToTask();

        if (proceed == false)
        {
            IsPathsReadOnly = false;
            return;
        }

        try
        {
            //var r = await MsgBoxError.Handle(new string[] { "Neco se posralo!", "Tak to ne otot toto se ale nemelo posrat ale posralo se to ajaj!" }).ToTask();
            //var rr = await MsgBoxInfo.Handle(new string[] { "Neco se stalo!", "Neco se stalo atak dale jo." }).ToTask();
            //var res = await MsgBoxYesNo.Handle(new string[] { "Chcete skibidy toilet?!", "Jste si jisti ze kibidi bididis?" }).ToTask();

            if (Directory.Exists(BanksPath) == false)
            {
                await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    "Provided Banks Path doesn't exist!" }).ToTask();
                return;
            }

            if (Directory.Exists(WavsPath) == false)
            {
                await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    "Provided Wav Path doesn't exist!" }).ToTask();
                return;
            }

            var files = Directory.EnumerateFiles(BanksPath)
                .ToList()
                .Where(x => x.EndsWith(".bank"))
                .ToList();

            if (files.Count() == 0)
            {
                await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    "No Sound Banks Were Found!" }).ToTask();
                return;
            }

            foreach (var file in files)
            {

                var bankDirName = Path.GetFileNameWithoutExtension(file);

                ProgressText = $"Processing: {bankDirName}.bank";
                ProgressMaximum = 1;
                ProgressValue = 0;

                var wavDir = Path.Combine(WavsPath, bankDirName);

                var fsbFile = Path.Combine(FsbPath, bankDirName) + ".fsb";

                // First we get .fsb from .bank

                if (await ExtractFSB(file, fsbFile) == false)
                {
                    throw new Exception("Error while extracting bank.");
                }

                if (Directory.Exists(wavDir))
                {
                    Directory.Delete(wavDir, true);
                }

                Directory.CreateDirectory(wavDir);

                using var f = File.OpenRead(fsbFile);
                using var memory = memoryStreamManager.GetStream();

                await f.CopyToAsync(memory);
                memory.Position = 0;
                try
                {
                    FmodSoundBank bank = FsbLoader.LoadFsbFromByteArray(memory.GetBuffer());
                    var listFileText = new StringBuilder();

                    ProgressMaximum = bank.Samples.Count;
                    ProgressValue = 0;

                    foreach (var sample in bank.Samples)
                    {
                        ProgressValue++;
                        if (sample == null)
                            continue;

                        if (sample.RebuildAsStandardFileFormat(out var dataBytes, out var fileExtension))
                        {
                            var filename = sample.Name + "." + fileExtension;
                            listFileText.AppendLine(filename);

                            var sampleFileName = Path.Combine(wavDir, filename);
                            using var sampleFile = File.OpenWrite(sampleFileName);

                            sampleFile.Write(dataBytes);
                        }

                        //await Task.Delay(100);
                    }

                    var lstFilename = Path.Combine(wavDir, "files.lst");
                    try
                    {
                        File.Delete(lstFilename);
                    }
                    catch (Exception)
                    {
                    }

                    using var lstFile = File.OpenWrite(lstFilename);
                    using var lstFileWriter = new StreamWriter(lstFile);

                    await lstFileWriter.WriteAsync(listFileText.ToString());
                }
                catch (Exception ex)
                {
                    await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    $"Error: {ex.Message}" }).ToTask();
                    return;
                }
            }

            GC.Collect();
        }
        finally
        {
            IsPathsReadOnly = false;
            ProgressText = "Idle";
            ProgressMaximum = 1;
            ProgressValue = 1;
        }
    }

    async Task RebuildBanks()
    {
        IsPathsReadOnly = true;

        var proceed = await MsgBoxYesNo.Handle(new string[] {
                    "Warning!",
                    "Following operation will overwrite existing rebuild BANK files! Do you want to proceed?" }).ToTask();

        if (proceed == false)
        {
            IsPathsReadOnly = false;
            return;
        }

        try
        {
            if (Directory.Exists(WavsPath) == false)
            {
                await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    "Provided Wav Path doesn't exist!" }).ToTask();
                return;
            }

            var wavDirs = Directory.EnumerateDirectories(WavsPath).ToList();

            wavDirs = wavDirs.Where(x =>
            {
                var exist = File.Exists(Path.Combine(x, "files.lst"));
                return exist;
            }).ToList();

            if (wavDirs.Count == 0)
            {
                await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    "No suitable folders were found!" }).ToTask();
                return;
            }

            ProgressMaximum = wavDirs.Count;
            ProgressValue = 0;
            foreach (var wavDir in wavDirs)
            {
                var bankName = Path.GetFileNameWithoutExtension(wavDir);

                ProgressText = $"Building {bankName}.bank";
                ProgressValue++;

                if (bankName == null)
                {
                    await MsgBoxError.Handle(new string[] {
                    "Something Bad Happened!",
                    "Bank Name was null!" }).ToTask();
                    return;
                }

                var fsbFile = Path.Combine(FsbPath, bankName) + ".fsb";

                var lstFile = Path.Combine(wavDir, "files.lst");

                var threadCount = Environment.ProcessorCount;

                // async iterate.
                await foreach (string item in ProcessX.StartAsync(
                    $"FMOD\\fsbankcl.exe -rebuild -thread_count {threadCount} -format Vorbis -ignore_errors -quality 85 -verbosity 5 -o {fsbFile} {lstFile}"))
                {
                    await AddConsoleLine.Handle(item).ToTask();
                }

                var originalBankPath = Path.Combine(BanksPath, bankName) + ".bank";
                using var originalBank = File.OpenRead(originalBankPath);
                using var originalMemory = memoryStreamManager.GetStream();

                await originalBank.CopyToAsync(originalMemory);

                using var orignalBankReader = new BinaryReader(originalMemory);

                originalMemory.Position = 0;

                var headerOffset = Search(originalMemory.GetBuffer(), Encoding.ASCII.GetBytes("SNDH"));

                originalMemory.Position = headerOffset + 12;
                var headerSize = orignalBankReader.ReadInt32();

                //var originalHeaderBytes = orignalBankReader.ReadBytes(headerSize);

                var fsbFileSize = (int)new FileInfo(fsbFile).Length;

                var modifiedBankPath = Path.Combine(BuildPath, bankName) + ".bank";

                try
                {
                    File.Delete(modifiedBankPath);
                }
                catch (Exception)
                {

                }

                using var modifiedBankFile = File.OpenWrite(modifiedBankPath);
                using var modifiedBankWriter = new BinaryWriter(modifiedBankFile);

                // copy original header to our new bank
                originalMemory.Position = 0;
                await modifiedBankFile.WriteAsync(originalMemory.GetBuffer(), 0, headerSize);
                var headerEndOffset = modifiedBankFile.Position;

                modifiedBankWriter.Seek(4, SeekOrigin.Begin);
                // Write new total file size
                modifiedBankWriter.Write((int)(headerSize + fsbFileSize - 8));
                modifiedBankWriter.Seek(headerOffset + 16, SeekOrigin.Begin);
                // write new offset for new FSB file
                modifiedBankWriter.Write((int)(headerSize + fsbFileSize - (headerSize + 8)));
                modifiedBankWriter.Seek(Convert.ToInt32(headerEndOffset), SeekOrigin.Begin);

                using var modifiedFsbFile = File.OpenRead(fsbFile);

                //write the FSB content
                await modifiedFsbFile.CopyToAsync(modifiedBankFile);
            }
        }
        finally
        {
            IsPathsReadOnly = false;
            ProgressText = "Idle";
            ProgressMaximum = 1;
            ProgressValue = 1;
        }
    }
}
