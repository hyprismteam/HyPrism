// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPrism.Core.Accounts;
using HyPrism.Core.Models;
using HyPrism.Desktop.Localization;
using HyPrism.Desktop.Platform;

namespace HyPrism.Desktop.Features.Profiles;

/// <summary>
/// Provides the native profile manager page and its profile creation flows.
/// </summary>
public sealed partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private static readonly Regex OfflineNamePattern = new(
        "^[a-zA-Z0-9_-]{3,16}$",
        RegexOptions.CultureInvariant);

    private readonly IProfileManager _profileManager;
    private readonly IProfileRepository _profileRepository;
    private readonly IHytaleAuthenticator? _authenticator;
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly StringLocalizer _localizer;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(IsProfileEditorVisible))]
    [NotifyPropertyChangedFor(nameof(CanActivateSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(ActivationLabel))]
    private ProfileItemViewModel? _selectedProfile;

    [ObservableProperty]
    private bool _isCreateChoiceVisible;

    [ObservableProperty]
    private bool _isOfflineCreationVisible;

    [ObservableProperty]
    private bool _isOfficialCreationVisible;

    [ObservableProperty]
    private bool _isCreationVisible;

    [ObservableProperty]
    private bool _isAuthenticating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateOfflineProfile))]
    private string _offlineProfileName = GenerateDefaultOfflineName();

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editUuid = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isStatusError;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingProfileDeletion))]
    private ProfileItemViewModel? _pendingProfileDeletion;

    public ProfilesViewModel(
        IProfileManager profileManager,
        IProfileRepository profileRepository,
        IExternalUriLauncher uriLauncher,
        StringLocalizer localizer,
        IHytaleAuthenticator? authenticator = null)
    {
        _profileManager = profileManager;
        _profileRepository = profileRepository;
        _uriLauncher = uriLauncher;
        _localizer = localizer;
        _authenticator = authenticator;

        RefreshProfiles();
    }

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    public event EventHandler? ActiveProfileChanged;

    public bool HasSelectedProfile => SelectedProfile is not null;
    public bool HasProfiles => Profiles.Count > 0;
    public bool HasNoProfiles => !HasProfiles;
    public bool IsEmptyStateVisible => HasNoProfiles;
    public bool IsProfileEditorVisible => SelectedProfile is not null;
    public bool CanCreateOfflineProfile => OfflineNamePattern.IsMatch(OfflineProfileName.Trim());
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasPendingProfileDeletion => PendingProfileDeletion is not null;
    public bool CanActivateSelectedProfile => SelectedProfile is { IsActive: false };

    public string SavedProfilesLabel => _localizer["profiles.savedProfiles"];
    public string EditorLabel => _localizer["profiles.editor"];
    public string CreateProfileLabel => _localizer["profiles.wizard.title"];
    public string CreateProfileHint => _localizer["profiles.wizard.chooseType"];
    public string OfflineProfileLabel => _localizer["profiles.wizard.unofficial"];
    public string OfflineProfileHint => _localizer["profiles.wizard.unofficialDesc"];
    public string OfficialProfileLabel => _localizer["profiles.wizard.official"];
    public string OfficialProfileHint => _localizer["profiles.wizard.officialDesc"];
    public string ProfileNameLabel => _localizer["profileEditor.username"];
    public string ProfileNameHint => _localizer["profileEditor.usernameHint"];
    public string UuidLabel => _localizer["profileEditor.uuid"];
    public string UuidHint => _localizer["profileEditor.uuidHint"];
    public string NamePlaceholder => _localizer["profiles.wizard.namePlaceholder"];
    public string CreateOfflineTitle => _localizer["profiles.wizard.nameTitle"];
    public string CreateOfflineHint => _localizer["profiles.wizard.nameDesc"];
    public string AuthenticationTitle => _localizer["profiles.wizard.authTitle"];
    public string AuthenticationHint => _localizer["profiles.wizard.authDesc"];
    public string BrowserHint => _localizer["profiles.wizard.browserHint"];
    public string SignInLabel => _localizer["profiles.wizard.loginHytale"];
    public string WaitingForSignInLabel => _localizer["profiles.wizard.waitingAuth"];
    public string CreateLabel => _localizer["profiles.wizard.create"];
    public string AddLabel => _localizer["profiles.createNew"];
    public string CancelLabel => _localizer["common.cancel"];
    public string BackLabel => _localizer["common.back"];
    public string SaveLabel => _localizer["common.save"];
    public string EditLabel => _localizer["common.edit"];
    public string CopyLabel => _localizer["profiles.copyUuid"];
    public string FolderLabel => _localizer["profiles.openFolder"];
    public string DeleteActionLabel => _localizer["common.delete"];
    public string ActivationLabel => _localizer["profiles.setActive"];
    public string ActiveLabel => _localizer["profiles.active"];
    public string DeleteLabel => _localizer["profiles.deleteProfile"];
    public string DuplicateLabel => _localizer["profiles.duplicateProfile"];
    public string RandomizeNameLabel => _localizer["profiles.generateName"];
    public string RandomizeUuidLabel => _localizer["profiles.randomUuid"];
    public string OfficialLockedLabel => _localizer["profiles.officialLocked"];
    public string NoProfilesLabel => _localizer["profiles.noProfiles"];
    public string DeleteTitle => _localizer["deleteProfile.title"];
    public string DeleteHint => _localizer["deleteProfile.cannotUndo"];
    public string OfflineNameRuleLabel => _localizer["profiles.wizard.nickRules"];

    /// <summary>
    /// Reloads the saved profile list and retains the currently displayed item when possible.
    /// </summary>
    public void RefreshProfiles(string? preferredProfileId = null)
    {
        if (_disposed)
            return;

        var selectedId = preferredProfileId ?? SelectedProfile?.Id;
        List<Profile> profileList;
        try
        {
            profileList = _profileRepository.GetProfiles() ?? [];
        }
        catch
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        foreach (var profile in Profiles)
            profile.Dispose();
        Profiles.Clear();

        var activeProfileId = _profileRepository.GetSelectedProfileId();
        foreach (var profile in profileList)
        {
            Profiles.Add(new ProfileItemViewModel(
                profile.Id,
                profile.Name,
                profile.UUID,
                profile.IsOfficial,
                string.Equals(profile.Id, activeProfileId, StringComparison.Ordinal),
                string.Equals(profile.Id, selectedId, StringComparison.Ordinal),
                profile.IsOfficial ? OfficialProfileLabel : OfflineProfileLabel,
                LoadAvatar(profile.UUID)));
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
        OnPropertyChanged(nameof(IsEmptyStateVisible));

        if (IsCreationVisible)
            return;

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == selectedId) ??
                          Profiles.FirstOrDefault(profile => profile.IsActive);
    }

    public void RefreshLocalization()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(SavedProfilesLabel), nameof(EditorLabel), nameof(CreateProfileLabel),
                     nameof(CreateProfileHint), nameof(OfflineProfileLabel), nameof(OfflineProfileHint),
                     nameof(OfficialProfileLabel), nameof(OfficialProfileHint), nameof(ProfileNameLabel),
                     nameof(ProfileNameHint), nameof(UuidLabel), nameof(UuidHint), nameof(NamePlaceholder),
                     nameof(CreateOfflineTitle), nameof(CreateOfflineHint), nameof(AuthenticationTitle),
                     nameof(AuthenticationHint), nameof(BrowserHint), nameof(SignInLabel),
                     nameof(WaitingForSignInLabel), nameof(CreateLabel), nameof(AddLabel),
                     nameof(CancelLabel), nameof(BackLabel), nameof(SaveLabel), nameof(EditLabel),
                     nameof(CopyLabel), nameof(FolderLabel), nameof(DeleteActionLabel), nameof(ActivationLabel), nameof(ActiveLabel), nameof(DeleteLabel), nameof(DuplicateLabel),
                     nameof(RandomizeNameLabel), nameof(RandomizeUuidLabel), nameof(OfficialLockedLabel),
                     nameof(NoProfilesLabel), nameof(DeleteTitle), nameof(DeleteHint), nameof(OfflineNameRuleLabel)
                 })
        {
            OnPropertyChanged(propertyName);
        }

        RefreshProfiles(SelectedProfile?.Id);
    }

    public void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    [RelayCommand]
    private void SelectProfile(ProfileItemViewModel? profile)
    {
        if (profile is null)
            return;

        CloseProfileMenus();
        ClearStatus();
        IsCreationVisible = false;
        IsCreateChoiceVisible = false;
        IsOfflineCreationVisible = false;
        IsOfficialCreationVisible = false;
        IsEditing = false;

        SelectedProfile = profile;
    }

    [RelayCommand]
    private void ActivateSelectedProfile()
    {
        var profile = SelectedProfile;
        if (profile is null || profile.IsActive)
            return;

        if (!_profileRepository.SwitchProfile(profile.Id))
        {
            SetStatus(_localizer["profiles.wizard.createFailed"], isError: true);
            RefreshProfiles(profile.Id);
            return;
        }

        _authenticator?.ReloadSessionForCurrentProfile();
        RefreshProfiles(profile.Id);
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowCreateChoice()
    {
        CloseProfileMenus();
        ClearStatus();
        IsEditing = false;
        IsOfflineCreationVisible = false;
        IsOfficialCreationVisible = false;
        IsCreateChoiceVisible = true;
        IsCreationVisible = true;
    }

    [RelayCommand]
    private void BeginOfflineCreation()
    {
        ClearStatus();
        OfflineProfileName = GenerateDefaultOfflineName();
        IsCreateChoiceVisible = false;
        IsOfficialCreationVisible = false;
        IsOfflineCreationVisible = true;
    }

    [RelayCommand]
    private void BeginOfficialCreation()
    {
        ClearStatus();
        IsCreateChoiceVisible = false;
        IsOfflineCreationVisible = false;
        IsOfficialCreationVisible = true;
    }

    [RelayCommand]
    private void CancelCreation()
    {
        ClearStatus();
        IsCreationVisible = false;
        RefreshProfiles();
    }

    internal void CompleteCreationTransition()
    {
        if (IsCreationVisible)
            return;

        IsCreateChoiceVisible = false;
        IsOfflineCreationVisible = false;
        IsOfficialCreationVisible = false;
    }

    [RelayCommand]
    private void ReturnToCreationChoice()
    {
        ClearStatus();
        IsOfflineCreationVisible = false;
        IsOfficialCreationVisible = false;
        IsCreateChoiceVisible = true;
    }

    [RelayCommand]
    private void GenerateOfflineProfileName()
        => OfflineProfileName = GenerateDefaultOfflineName();

    [RelayCommand]
    private void CreateOfflineProfile()
    {
        var name = OfflineProfileName.Trim();
        if (!OfflineNamePattern.IsMatch(name))
        {
            SetStatus(_localizer["profiles.wizard.nickInvalid"], isError: true);
            return;
        }

        var profile = _profileRepository.CreateProfile(name, Guid.NewGuid().ToString());
        if (profile is null || !_profileRepository.SwitchProfile(profile.Id))
        {
            SetStatus(_localizer["profiles.wizard.createFailed"], isError: true);
            return;
        }

        IsCreationVisible = false;
        RefreshProfiles(profile.Id);
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(_localizer["profiles.saved"], isError: false);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SignInWithHytaleAsync()
    {
        if (_authenticator is null || IsAuthenticating)
        {
            SetStatus(_localizer["profiles.wizard.authFailed"], isError: true);
            return;
        }

        IsAuthenticating = true;
        ClearStatus();
        try
        {
            var session = await _authenticator.LoginAsync(_uriLauncher.LaunchAsync);
            if (session is null)
            {
                SetStatus(_localizer["profiles.wizard.authFailed"], isError: true);
                return;
            }

            var identities = session.AccountProfiles.Count > 0
                ? session.AccountProfiles
                : [(session.Username, session.UUID)];
            var existingProfiles = _profileRepository.GetProfiles() ?? [];
            Profile? firstProfile = null;

            foreach (var (username, uuid) in identities)
            {
                if (string.IsNullOrWhiteSpace(username) || !Guid.TryParse(uuid, out _))
                    continue;

                var profile = existingProfiles.FirstOrDefault(existing =>
                                  existing.IsOfficial &&
                                  string.Equals(existing.UUID, uuid, StringComparison.OrdinalIgnoreCase)) ??
                              _profileRepository.CreateProfile(username, uuid, isOfficial: true);
                if (profile is null)
                    continue;

                _authenticator.SaveSessionToProfile(profile);
                firstProfile ??= profile;
            }

            if (firstProfile is null || !_profileRepository.SwitchProfile(firstProfile.Id))
            {
                SetStatus(_localizer["profiles.wizard.createFailed"], isError: true);
                return;
            }

            _authenticator.ReloadSessionForCurrentProfile();
            IsCreationVisible = false;
            RefreshProfiles(firstProfile.Id);
            ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
            SetStatus(_localizer["profiles.saved"], isError: false);
        }
        catch
        {
            SetStatus(_localizer["profiles.wizard.authError"], isError: true);
        }
        finally
        {
            IsAuthenticating = false;
        }
    }

    [RelayCommand]
    private void BeginEditing()
    {
        if (SelectedProfile is null || SelectedProfile.IsOfficial)
            return;

        ClearStatus();
        EditName = SelectedProfile.Name;
        EditUuid = SelectedProfile.Uuid;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEditing()
    {
        if (SelectedProfile is not null)
        {
            EditName = SelectedProfile.Name;
            EditUuid = SelectedProfile.Uuid;
        }
        IsEditing = false;
        ClearStatus();
    }

    [RelayCommand]
    private void RandomizeEditName()
        => EditName = GenerateDefaultOfflineName();

    [RelayCommand]
    private void RandomizeEditUuid()
        => EditUuid = Guid.NewGuid().ToString();

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is null || SelectedProfile.IsOfficial)
            return;

        var name = EditName.Trim();
        var uuid = EditUuid.Trim();
        if (name.Length is < 1 or > 16 || !Guid.TryParse(uuid, out _))
        {
            SetStatus(_localizer["profiles.wizard.nickInvalid"], isError: true);
            return;
        }

        if (!_profileRepository.UpdateProfile(SelectedProfile.Id, name, uuid))
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        IsEditing = false;
        RefreshProfiles(SelectedProfile.Id);
        ActiveProfileChanged?.Invoke(this, EventArgs.Empty);
        SetStatus(_localizer["profiles.saved"], isError: false);
    }

    [RelayCommand]
    private async Task OpenProfileFolderAsync()
    {
        var selectedId = SelectedProfile?.Id;
        var profile = _profileRepository.GetProfiles()
            .FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal));
        var path = profile is null ? null : _profileManager.GetProfilePath(profile);
        if (string.IsNullOrWhiteSpace(path) || !await _uriLauncher.LaunchDirectoryAsync(path))
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
    }

    public void MoveProfile(string profileId, int targetIndex)
    {
        var profile = Profiles.FirstOrDefault(
            item => string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
            return;

        var sourceIndex = Profiles.IndexOf(profile);
        targetIndex = Math.Clamp(targetIndex, 0, Profiles.Count - 1);
        if (sourceIndex == targetIndex)
            return;

        Profiles.Move(sourceIndex, targetIndex);
        _profileRepository.SetProfileOrder(Profiles.Select(item => item.Id).ToList());
    }

    [RelayCommand]
    private void DuplicateProfile(ProfileItemViewModel? profile)
    {
        if (profile is null)
            return;

        if (_profileRepository.DuplicateProfileWithoutData(profile.Id) is null)
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        RefreshProfiles(profile.Id);
        SetStatus(_localizer["profiles.saved"], isError: false);
    }

    [RelayCommand]
    private void RequestProfileDeletion(ProfileItemViewModel? profile)
    {
        if (profile is null || profile.IsActive)
            return;

        PendingProfileDeletion = profile;
    }

    [RelayCommand]
    private void CancelProfileDeletion()
        => PendingProfileDeletion = null;

    [RelayCommand]
    private void ConfirmProfileDeletion()
    {
        var profile = PendingProfileDeletion;
        PendingProfileDeletion = null;
        if (profile is null)
            return;

        if (!_profileRepository.DeleteProfile(profile.Id))
        {
            SetStatus(_localizer["profiles.wizard.createError"], isError: true);
            return;
        }

        RefreshProfiles();
        SetStatus(_localizer["profiles.saved"], isError: false);
    }

    private void ClearStatus()
    {
        StatusMessage = string.Empty;
        IsStatusError = false;
    }

    private void CloseProfileMenus()
    {
        foreach (var profile in Profiles)
            profile.IsMenuOpen = false;
    }

    private Bitmap? LoadAvatar(string uuid)
    {
        try
        {
            var preview = _profileManager.GetAvatarPreviewForUUID(uuid);
            if (string.IsNullOrWhiteSpace(preview))
                return null;

            var separator = preview.IndexOf(',');
            if (separator < 0 || separator == preview.Length - 1)
                return null;

            var bytes = Convert.FromBase64String(preview[(separator + 1)..]);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateDefaultOfflineName()
        => $"Prism{Random.Shared.Next(1000, 10000)}";

    partial void OnSelectedProfileChanged(ProfileItemViewModel? value)
    {
        foreach (var profile in Profiles)
            profile.IsSelected = ReferenceEquals(profile, value);

        EditName = value?.Name ?? string.Empty;
        EditUuid = value?.Uuid ?? string.Empty;
        IsEditing = false;
    }

    partial void OnOfflineProfileNameChanged(string value)
        => OnPropertyChanged(nameof(CanCreateOfflineProfile));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var profile in Profiles)
            profile.Dispose();
        Profiles.Clear();
    }
}
