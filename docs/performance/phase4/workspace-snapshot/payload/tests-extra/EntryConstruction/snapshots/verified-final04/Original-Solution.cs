using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EntryConstruction.Contracts;
using FileExtensionHelpers = Files.Shared.Helpers.Original.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public sealed class OriginalSolution
{
    private readonly object loadCollectionSync = new();
    private readonly List<SolutionItem> filesAndFolders = new();
    private string? solutionFilePath;
    public bool SetterRanInsideCollectionLock { get; private set; }
    public string? SolutionFilePath
    {
        get => solutionFilePath;
        set { SetterRanInsideCollectionLock = Monitor.IsEntered(loadCollectionSync); solutionFilePath = value; }
    }
    public void SetItems(params SolutionItem[] items)
    {
        filesAndFolders.Clear();
        foreach (var item in items)
        {
            item.RequiredLock = false ? loadCollectionSync : null;
            filesAndFolders.Add(item);
        }
    }
    public void Run() => CheckForSolutionFile();
    private void CheckForSolutionFile()
		{
			SolutionFilePath = filesAndFolders.ToList().AsParallel()
				.Where(item => FileExtensionHelpers.HasExtension(item.FileExtension, ".sln", ".slnx"))
				.FirstOrDefault()?.ItemPath;
		}
}