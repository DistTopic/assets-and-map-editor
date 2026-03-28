using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetsAndMapEditor.OTB;

namespace AssetsAndMapEditor.App.ViewModels;

public partial class ItemViewModel : ObservableObject
{
    private readonly OtbItem _model;
    private readonly MainWindowViewModel _parent;

    public OtbItem Model => _model;

#pragma warning disable MVVMTK0034
    public ItemViewModel(OtbItem model, MainWindowViewModel parent)
    {
        _model = model;
        _parent = parent;
        // Snapshot from model
        _serverId = model.ServerId;
        _clientId = model.ClientId;
        _speed = model.Speed;
        _wareId = model.WareId;
        _lightLevel = model.LightLevel;
        _lightColor = model.LightColor;
        _topOrder = model.TopOrder;
        _minimapColor = model.MinimapColor;
        _name = model.Name ?? string.Empty;
        _group = model.Group;
        _isAnimation = model.IsAnimation;
        _isStackable = model.Flags.HasFlag(OtbFlags.Stackable);
        _isPickupable = model.Flags.HasFlag(OtbFlags.Pickupable);
        _isMoveable = model.Flags.HasFlag(OtbFlags.Moveable);
        _isBlockSolid = model.Flags.HasFlag(OtbFlags.BlockSolid);
        _isBlockProjectile = model.Flags.HasFlag(OtbFlags.BlockProjectile);
        _isBlockPathFind = model.Flags.HasFlag(OtbFlags.BlockPathFind);
        _isHasHeight = model.Flags.HasFlag(OtbFlags.HasHeight);
        _isUsable = model.Flags.HasFlag(OtbFlags.Usable);
        _isHangable = model.Flags.HasFlag(OtbFlags.Hangable);
        _isRotatable = model.Flags.HasFlag(OtbFlags.Rotatable);
        _isReadable = model.Flags.HasFlag(OtbFlags.Readable);
        _isLookThrough = model.Flags.HasFlag(OtbFlags.LookThrough);
        _isForceUse = model.Flags.HasFlag(OtbFlags.ForceUse);
        _isAlwaysOnTop = model.TopOrder != 0; // Derived from TopOrder — must stay in sync
        _isVertical = model.Flags.HasFlag(OtbFlags.Vertical);
        _isHorizontal = model.Flags.HasFlag(OtbFlags.Horizontal);
        _isAllowDistRead = model.Flags.HasFlag(OtbFlags.AllowDistRead);
        _isFullGround = model.Flags.HasFlag(OtbFlags.FullGround);
        _isClientCharges = model.Flags.HasFlag(OtbFlags.ClientCharges);
        // Preserve flags we don't expose as checkboxes
        _preservedFlags = model.Flags & (OtbFlags.FloorChangeDown | OtbFlags.FloorChangeNorth
            | OtbFlags.FloorChangeEast | OtbFlags.FloorChangeSouth | OtbFlags.FloorChangeWest
            | OtbFlags.CanNotDecay | OtbFlags.Unused);
    }
#pragma warning restore MVVMTK0034

    private readonly OtbFlags _preservedFlags;

    // ── Identity ────────────────────────────────────────────────────────

    [ObservableProperty] private ushort _serverId;
    [ObservableProperty] private ushort _clientId;
    [ObservableProperty] private OtbGroup _group;
    [ObservableProperty] private string _name;

    public string GroupName => Group.ToString();
    public string DisplayName => string.IsNullOrEmpty(Name) ? GroupName : Name;
    public bool IsDeprecated => Group == OtbGroup.Deprecated;

    // ── Attributes ──────────────────────────────────────────────────────

    [ObservableProperty] private ushort _speed;
    [ObservableProperty] private ushort _wareId;
    [ObservableProperty] private ushort _lightLevel;
    [ObservableProperty] private ushort _lightColor;
    [ObservableProperty] private byte _topOrder;
    [ObservableProperty] private ushort _minimapColor;

    public SolidColorBrush MinimapColorBrush => TibiaColors.BrushFrom8Bit(MinimapColor);

    [RelayCommand]
    private void IncrementClientId()
    {
        if (ClientId < ushort.MaxValue) ClientId++;
    }

    [RelayCommand]
    private void DecrementClientId()
    {
        if (ClientId > 0) ClientId--;
    }

    // ── Sprite ──────────────────────────────────────────────────────────

    [ObservableProperty] private WriteableBitmap? _sprite;

    // ── Flags ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isAnimation;
    [ObservableProperty] private bool _isStackable;
    [ObservableProperty] private bool _isPickupable;
    [ObservableProperty] private bool _isMoveable;
    [ObservableProperty] private bool _isBlockSolid;
    [ObservableProperty] private bool _isBlockProjectile;
    [ObservableProperty] private bool _isBlockPathFind;
    [ObservableProperty] private bool _isHasHeight;
    [ObservableProperty] private bool _isUsable;
    [ObservableProperty] private bool _isHangable;
    [ObservableProperty] private bool _isRotatable;
    [ObservableProperty] private bool _isReadable;
    [ObservableProperty] private bool _isLookThrough;
    [ObservableProperty] private bool _isForceUse;
    [ObservableProperty] private bool _isAlwaysOnTop;
    [ObservableProperty] private bool _isVertical;
    [ObservableProperty] private bool _isHorizontal;
    [ObservableProperty] private bool _isAllowDistRead;
    [ObservableProperty] private bool _isFullGround;
    [ObservableProperty] private bool _isClientCharges;

    // ── DAT cross-reference ─────────────────────────────────────────────

    [ObservableProperty] private int _datAnimPhases;
    [ObservableProperty] private bool _datAnimateAlways;
    [ObservableProperty] private bool _hasMismatch;
    [ObservableProperty] private uint _firstSpriteId;

    /// <summary>Full DAT thing type (set during cross-reference).</summary>
    public DatThingType? DatThingType { get; set; }
    public int AnimFrame { get; set; }

    public string MismatchDetail
    {
        get
        {
            if (!HasMismatch) return string.Empty;
            if (DatAnimPhases > 1 && !IsAnimation)
                return $"DAT tem {DatAnimPhases} fases mas OTB Animation=false";
            if (DatAnimPhases <= 1 && IsAnimation)
                return "OTB Animation=true mas DAT tem ≤1 fase";
            return string.Empty;
        }
    }

    public string StackOrderName => TopOrder switch
    {
        1 => "Border",
        2 => "Bottom",
        3 => "Top",
        _ => "None",
    };

    public string StackOrderValue
    {
        get => StackOrderName;
        set
        {
            byte v = value switch
            {
                "Border" => 1,
                "Bottom" => 2,
                "Top" => 3,
                _ => 0,
            };
            TopOrder = v;
        }
    }

    // Notify mismatch detail when relevant properties change
    partial void OnHasMismatchChanged(bool value) => OnPropertyChanged(nameof(MismatchDetail));
    partial void OnDatAnimPhasesChanged(int value) => OnPropertyChanged(nameof(MismatchDetail));
    partial void OnGroupChanged(OtbGroup value)
    {
        _parent.MarkDirty();
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsDeprecated));
    }
    partial void OnIsAnimationChanged(bool value)
    {
        _parent.MarkDirty();
        OnPropertyChanged(nameof(MismatchDetail));
    }
    partial void OnTopOrderChanged(byte value)
    {
        _parent.MarkDirty();
        IsAlwaysOnTop = value != 0;
        OnPropertyChanged(nameof(StackOrderName));
        OnPropertyChanged(nameof(StackOrderValue));
    }

    // Notify parent on any flag/attr change
    partial void OnIsStackableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsPickupableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsMoveableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsBlockSolidChanged(bool value) => _parent.MarkDirty();
    partial void OnIsBlockProjectileChanged(bool value) => _parent.MarkDirty();
    partial void OnIsBlockPathFindChanged(bool value) => _parent.MarkDirty();
    partial void OnIsHasHeightChanged(bool value) => _parent.MarkDirty();
    partial void OnIsUsableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsHangableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsRotatableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsReadableChanged(bool value) => _parent.MarkDirty();
    partial void OnIsLookThroughChanged(bool value) => _parent.MarkDirty();
    partial void OnIsForceUseChanged(bool value) => _parent.MarkDirty();
    partial void OnIsAlwaysOnTopChanged(bool value) => _parent.MarkDirty();
    partial void OnIsVerticalChanged(bool value) => _parent.MarkDirty();
    partial void OnIsHorizontalChanged(bool value) => _parent.MarkDirty();
    partial void OnIsAllowDistReadChanged(bool value) => _parent.MarkDirty();
    partial void OnIsFullGroundChanged(bool value) => _parent.MarkDirty();
    partial void OnIsClientChargesChanged(bool value) => _parent.MarkDirty();
    partial void OnSpeedChanged(ushort value) => _parent.MarkDirty();
    partial void OnWareIdChanged(ushort value) => _parent.MarkDirty();
    partial void OnLightLevelChanged(ushort value) => _parent.MarkDirty();
    partial void OnLightColorChanged(ushort value) => _parent.MarkDirty();
    partial void OnMinimapColorChanged(ushort value)
    {
        _parent.MarkDirty();
        OnPropertyChanged(nameof(MinimapColorBrush));
    }
    partial void OnNameChanged(string value) => _parent.MarkDirty();

    /// <summary>Write VM state back into the underlying OtbItem model.</summary>
    public void ApplyToModel()
    {
        _model.ServerId = ServerId;
        _model.ClientId = ClientId;
        _model.Group = Group;
        _model.Speed = Speed;
        _model.WareId = WareId;
        _model.LightLevel = LightLevel;
        _model.LightColor = LightColor;
        _model.TopOrder = TopOrder;
        _model.MinimapColor = MinimapColor;
        _model.Name = string.IsNullOrEmpty(Name) ? null : Name;

        var flags = _preservedFlags; // Preserve FloorChange*, CanNotDecay, Unused
        if (IsAnimation) flags |= OtbFlags.Animation;
        if (IsStackable) flags |= OtbFlags.Stackable;
        if (IsPickupable) flags |= OtbFlags.Pickupable;
        if (IsMoveable) flags |= OtbFlags.Moveable;
        if (IsBlockSolid) flags |= OtbFlags.BlockSolid;
        if (IsBlockProjectile) flags |= OtbFlags.BlockProjectile;
        if (IsBlockPathFind) flags |= OtbFlags.BlockPathFind;
        if (IsHasHeight) flags |= OtbFlags.HasHeight;
        if (IsUsable) flags |= OtbFlags.Usable;
        if (IsHangable) flags |= OtbFlags.Hangable;
        if (IsRotatable) flags |= OtbFlags.Rotatable;
        if (IsReadable) flags |= OtbFlags.Readable;
        if (IsLookThrough) flags |= OtbFlags.LookThrough;
        if (IsForceUse) flags |= OtbFlags.ForceUse;
        if (TopOrder != 0) flags |= OtbFlags.AlwaysOnTop;
        if (IsVertical) flags |= OtbFlags.Vertical;
        if (IsHorizontal) flags |= OtbFlags.Horizontal;
        if (IsAllowDistRead) flags |= OtbFlags.AllowDistRead;
        if (IsFullGround) flags |= OtbFlags.FullGround;
        if (IsClientCharges) flags |= OtbFlags.ClientCharges;
        _model.Flags = flags;
    }
}
