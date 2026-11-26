using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.Views;

public partial class MainWindow
{
    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import packages or individual assets",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Packages and assets")
                {
                    Patterns = new[]
                    {
                        "*.zip", "*.png", "*.jpg", "*.jpeg", "*.dds", "*.tga",
                        "*.fbx", "*.obj", "*.glb", "*.dll", "*.lua", "*.json", "*.ini", "*.bin",
                    },
                },
                new FilePickerFileType("ZIP packages") { Patterns = new[] { "*.zip" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        await ImportPathsAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList());
    }

    private async Task ImportFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import unpacked mod folders",
            AllowMultiple = true,
        });

        if (folders.Count == 0)
        {
            return;
        }

        await ImportPathsAsync(folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToList());
    }

    private async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        int imported = 0;
        ModRecord? last = null;
        var errors = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                var (record, _) = await _services.Manager.ImportPackageAsync(path);
                last = record;
                imported++;
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}: {exception.Message}");
            }
        }

        RefreshAll();
        if (last is not null)
        {
            _selectedModId = last.Id;
            Go(1);
        }

        string message = $"Imported {imported} item(s).";
        if (errors.Count > 0)
        {
            message += "\n\nFailed:\n" + string.Join("\n", errors.Take(8));
        }

        if (imported > 0)
        {
            message += "\n\nOpen a package in the library to tune its variables or check its files.";
        }

        await Dialogs.Info(this, errors.Count == 0 ? "Import complete" : "Import finished with problems", message);
    }

    private async Task<string?> TargetDirOrWarnAsync(bool warn = true)
    {
        string path = _services.Settings.Current.TargetPath.Trim();
        if (path.Length == 0)
        {
            if (warn)
            {
                await Dialogs.Info(this, "Target folder required",
                    "Choose a target folder first.\n\nVariables can still be edited without one — they are saved with the package and written later.");
                Go(4);
            }

            return null;
        }

        if (!Directory.Exists(path))
        {
            if (warn)
            {
                await Dialogs.Info(this, "Target folder missing",
                    $"The saved target folder does not exist:\n{path}\n\nPick it again before deploying.");
                Go(4);
            }

            return null;
        }

        return path;
    }

    private async Task DeploySelectedAsync()
    {
        var mod = _services.Manager.Get(_selectedModId);
        if (mod is null)
        {
            return;
        }

        string? targetDir = await TargetDirOrWarnAsync();
        if (targetDir is null)
        {
            return;
        }

        try
        {
            LoadOutcome outcome = await _services.Manager.LoadModAsync(mod, targetDir);
            RefreshAll();
            var lines = new List<string> { $"{mod.Name} is now loaded." };
            lines.Add(outcome.ConfigPath is null
                ? "No config file declared — variables stay saved in the app."
                : $"Variables written to {outcome.ConfigPath} in the target folder.");
            await Dialogs.Info(this, "Package deployed", string.Join("\n", lines));
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Could not load mod", exception.Message);
        }
    }

    private async Task LoadAllAsync()
    {
        string? targetDir = await TargetDirOrWarnAsync();
        if (targetDir is null)
        {
            return;
        }

        try
        {
            LoadSummary summary = await _services.Manager.LoadAllAsync(targetDir);
            RefreshAll();
            var lines = new List<string> { $"{summary.Loaded.Count} mod(s) loaded in order." };
            if (summary.Skipped.Count > 0)
            {
                lines.Add("\nSkipped:\n" + string.Join("\n", summary.Skipped.Take(6).Select(item => $"• {item.Name}: {item.Why}")));
            }

            if (summary.Failed.Count > 0)
            {
                lines.Add("\nFailed:\n" + string.Join("\n", summary.Failed.Take(6).Select(item => $"• {item.Name}: {item.Why}")));
            }

            await Dialogs.Info(this, summary.Failed.Count > 0 ? "Finished with problems" : "Load complete",
                string.Join("\n", lines));
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Could not load mods", exception.Message);
        }
    }

    private async Task UnloadAllAsync()
    {
        string? targetDir = await TargetDirOrWarnAsync();
        if (targetDir is null)
        {
            return;
        }

        var loaded = _services.Manager.LoadOrder().Where(mod => mod.Installed).ToList();
        if (loaded.Count == 0)
        {
            await Dialogs.Info(this, "Nothing to unload", "No mods are currently loaded.");
            return;
        }

        bool confirmed = await Dialogs.Confirm(this, "Unload all",
            $"Unload {loaded.Count} mod(s) and restore every backed-up file?\n\nMods are unloaded in reverse load order.",
            "Unload all", true);
        if (!confirmed)
        {
            return;
        }

        int restored = 0;
        var errors = new List<string>();
        foreach (var mod in loaded.AsEnumerable().Reverse())
        {
            try
            {
                restored += await _services.Manager.UninstallAsync(mod, targetDir);
            }
            catch (Exception exception)
            {
                errors.Add($"{mod.Name}: {exception.Message}");
            }
        }

        RefreshAll();
        string message = $"Unloaded {loaded.Count - errors.Count} mod(s). {restored} original file(s) restored.";
        if (errors.Count > 0)
        {
            message += "\n\nFailed:\n" + string.Join("\n", errors.Take(8));
        }

        await Dialogs.Info(this, errors.Count == 0 ? "Unload complete" : "Unload finished with problems", message);
    }

    private void ToggleSelected(ModRecord mod)
    {
        _services.Manager.Toggle(mod, !mod.Enabled);
        RefreshAll();
    }

    private void VerifySelected(ModRecord mod)
    {
        string package = Path.Combine(_services.ModsDir, mod.Package);
        if (!File.Exists(package))
        {
            _ = Dialogs.Info(this, "Verification failed", "The package file is missing.");
            return;
        }

        _ = Dialogs.Info(this, "Package verified",
            Hashing.Sha256File(package) == mod.Sha256
                ? "SHA-256 matches the imported package record."
                : "SHA-256 mismatch. The package may have changed or become corrupted.");
    }

    private async Task RenameAsync(ModRecord mod)
    {
        string? name = await Dialogs.Prompt(this, "Rename mod", "Mod name:", mod.Name);
        if (name is null || name.Trim().Length == 0)
        {
            return;
        }

        _services.Manager.Rename(mod, name);
        RefreshAll();
    }

    private async Task UnloadAsync(ModRecord mod)
    {
        string? targetDir = await TargetDirOrWarnAsync();
        if (targetDir is null)
        {
            return;
        }

        bool confirmed = await Dialogs.Confirm(this, "Unload mod",
            "Unload this mod and restore the files it backed up?", "Unload");
        if (!confirmed)
        {
            return;
        }

        try
        {
            int restored = await _services.Manager.UninstallAsync(mod, targetDir);
            RefreshAll();
            await Dialogs.Info(this, "Package unloaded", $"Unload complete. {restored} original file(s) restored.");
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Unload failed", exception.Message);
        }
    }

    private async Task RemoveAsync(ModRecord mod)
    {
        bool confirmed = await Dialogs.Confirm(this, "Remove mod",
            "Remove this package from the library?\n\nUnload it first if it is currently deployed.", "Remove", true);
        if (!confirmed)
        {
            return;
        }

        try
        {
            _services.Manager.Remove(mod);
            _selectedModId = null;
            RefreshAll();
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Cannot remove", exception.Message);
        }
    }

    private async Task SaveVariablesAsync(ModRecord mod)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, read) in _fieldReaders)
        {
            values[field.Key] = read();
        }

        foreach (var (_, keyBox, valueBox) in _customRows)
        {
            string key = keyBox.Text?.Trim() ?? "";
            if (key.Length > 0)
            {
                values[key] = valueBox.Text ?? "";
            }
        }

        try
        {
            _services.Manager.SaveConfig(mod, values);
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Could not save variables", exception.Message);
            return;
        }

        RefreshAll();
        await Dialogs.Info(this, "Variables saved",
            "Values are stored with this mod and written to the target folder whenever it is loaded.");
    }

    private async Task ResetVariablesAsync(ModRecord mod)
    {
        bool confirmed = await Dialogs.Confirm(this, "Reset variables",
            "Reset every variable of this mod to its default value?", "Reset");
        if (!confirmed)
        {
            return;
        }

        _services.Manager.ResetConfig(mod);
        RefreshAll();
    }

    private void MoveMod(string modId, int delta)
    {
        var mod = _services.Manager.Get(modId);
        if (mod is null)
        {
            return;
        }

        if (_services.Manager.Move(mod, delta))
        {
            RefreshOrder();
            RefreshLibrary();
        }
    }

    private void MoveModTo(string modId, int target)
    {
        var mod = _services.Manager.Get(modId);
        if (mod is null)
        {
            return;
        }

        if (_services.Manager.MoveTo(mod, target))
        {
            RefreshOrder();
            RefreshLibrary();
        }
    }

    private async Task ShowConflictsAsync()
    {
        var conflicts = _services.Manager.Conflicts();
        if (conflicts.Count == 0)
        {
            await Dialogs.Info(this, "Conflict scan", "No manifest destination conflicts found.");
            return;
        }

        string text = string.Join("\n\n", conflicts.Take(15)
            .Select(conflict => $"{conflict.Destination}\n  {conflict.First} ↔ {conflict.Second}"));
        await Dialogs.Info(this, "Conflict scan", text);
    }

    private async Task ChooseTargetAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a target folder",
            AllowMultiple = false,
        });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        _services.Settings.Current.TargetPath = path;
        _services.Settings.Save();
        RefreshTarget();
    }

    private void SaveTarget()
    {
        _services.Settings.Current.TargetPath = _targetPath.Text?.Trim() ?? "";
        _services.Settings.Save();
        RefreshTarget();
    }

    private async Task SaveProfileAsync()
    {
        string name = _profileName.Text?.Trim() ?? "";
        try
        {
            var profile = _services.Manager.SaveProfile(name);
            _profileName.Text = "";
            RefreshAll();
            await Dialogs.Info(this, "Profile saved",
                $"“{profile.Name}” now holds {profile.Enabled.Count} mod(s) and their variables.");
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Could not save profile", exception.Message);
        }
    }

    private async Task ApplyProfileAsync(ModProfile profile)
    {
        bool confirmed = await Dialogs.Confirm(this, "Apply profile",
            $"Apply “{profile.Name}”?\n\nEnabled flags and saved variables will be replaced.", "Apply");
        if (!confirmed)
        {
            return;
        }

        try
        {
            string? targetDir = await TargetDirOrWarnAsync(warn: false);
            int applied = _services.Manager.ApplyProfile(profile, targetDir);
            RefreshAll();
            await Dialogs.Info(this, "Profile applied",
                $"{applied} mod configuration(s) restored from “{profile.Name}”." +
                (targetDir is null ? "\n\nNo target folder set, so config files were not rewritten." : ""));
        }
        catch (Exception exception)
        {
            await Dialogs.Info(this, "Could not apply profile", exception.Message);
        }
    }

    private async Task RenameProfileAsync(ModProfile profile)
    {
        string? name = await Dialogs.Prompt(this, "Rename profile", "Profile name:", profile.Name);
        if (name is null || name.Trim().Length == 0)
        {
            return;
        }

        _services.Manager.RenameProfile(profile, name);
        RefreshProfiles();
    }

    private async Task DeleteProfileAsync(ModProfile profile)
    {
        bool confirmed = await Dialogs.Confirm(this, "Delete profile",
            $"Delete “{profile.Name}”? This does not change your mods.", "Delete", true);
        if (!confirmed)
        {
            return;
        }

        _services.Manager.DeleteProfile(profile);
        RefreshProfiles();
    }

    private async Task ClearHistoryAsync()
    {
        if (_services.History.Current.Entries.Count == 0)
        {
            return;
        }

        bool confirmed = await Dialogs.Confirm(this, "Clear activity log",
            "Remove all entries from the local activity history?", "Clear log", true);
        if (!confirmed)
        {
            return;
        }

        _services.Manager.ClearHistory();
        RefreshAll();
    }

    private static void OpenFolderInExplorer(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
