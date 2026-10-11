using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EntryConstruction.Contracts;
using FileExtensionHelpers = Files.Shared.Helpers.Current.FileExtensionHelpers;
namespace EntryConstruction.Generated;
public sealed class CurrentSolution
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
            item.RequiredLock = true ? loadCollectionSync : null;
            filesAndFolders.Add(item);
        }
    }
    public void Run() => CheckForSolutionFile();
    private void CheckForSolutionFile()
		{
			string? solutionPath = null;
			lock (loadCollectionSync)
			{
				foreach (var item in filesAndFolders)
				{
					if (string.Equals(item.FileExtension, ".sln", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(item.FileExtension, ".slnx", StringComparison.OrdinalIgnoreCase))
					{
						solutionPath = item.ItemPath;
						break;
					}
				}
			}
			SolutionFilePath = solutionPath;
		}
}