using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.WinForms;
using SysMat4 = System.Numerics.Matrix4x4;
using SysQuat = System.Numerics.Quaternion;
using SysVec3 = System.Numerics.Vector3;

namespace HKCLTool;

public enum PreviewPickKind
{
    Particle,
    Bone,
    Collider
}

public sealed class PreviewPickEventArgs : EventArgs
{
    public PreviewPickEventArgs(PreviewPickKind kind, int index, bool addToSelection)
    {
        Kind = kind;
        Index = index;
        AddToSelection = addToSelection;
    }

    public PreviewPickKind Kind { get; }
    public int Index { get; }
    public bool AddToSelection { get; }
}

public sealed class ParticleSelectionEventArgs : EventArgs
{
    public ParticleSelectionEventArgs(IReadOnlyList<int> particleIndices, bool addToSelection)
    {
        ParticleIndices = particleIndices;
        AddToSelection = addToSelection;
    }

    public IReadOnlyList<int> ParticleIndices { get; }
    public bool AddToSelection { get; }
}

public sealed class ParticleMoveEventArgs : EventArgs
{
    public ParticleMoveEventArgs(SysVec3 delta, bool localAxis)
    {
        Delta = delta;
        LocalAxis = localAxis;
    }

    public SysVec3 Delta { get; }
    public bool LocalAxis { get; }
}

public sealed class ParticleScaleEventArgs : EventArgs
{
    public ParticleScaleEventArgs(float factor, SysVec3? axis, bool localAxis, bool radiusOnly)
    {
        Factor = factor;
        Axis = axis;
        LocalAxis = localAxis;
        RadiusOnly = radiusOnly;
    }

    public float Factor { get; }
    public SysVec3? Axis { get; }
    public bool LocalAxis { get; }
    public bool RadiusOnly { get; }
}

public sealed class ParticleRotateEventArgs : EventArgs
{
    public ParticleRotateEventArgs(float radians, SysVec3 axis, bool localAxis)
    {
        Radians = radians;
        Axis = axis;
        LocalAxis = localAxis;
    }

    public float Radians { get; }
    public SysVec3 Axis { get; }
    public bool LocalAxis { get; }
}

public sealed class MirrorRequestedEventArgs : EventArgs
{
    public MirrorRequestedEventArgs(char axis, bool local)
    {
        Axis = axis;
        Local = local;
    }

    public char Axis { get; }
    public bool Local { get; }
}

public sealed class ViewportCameraState
{
    public float Yaw { get; init; }
    public float Pitch { get; init; }
    public float OrthoHeight { get; init; }
    public SysVec3 Target { get; init; }
}

public sealed class ParticlePreviewControl : GLControl
{
    private enum ParticleTransformMode
    {
        None,
        Move,
        Scale,
        Rotate
    }

    public event EventHandler<PreviewPickEventArgs>? ItemPicked;
    public event EventHandler<ParticleSelectionEventArgs>? ParticlesSelected;
    public event EventHandler? ParticleMoveStarted;
    public event EventHandler<ParticleMoveEventArgs>? ParticlesMoved;
    public event EventHandler<ParticleScaleEventArgs>? ParticlesScaled;
    public event EventHandler<ParticleRotateEventArgs>? ParticlesRotated;
    public event EventHandler<MirrorRequestedEventArgs>? MirrorRequested;
    public event EventHandler? MirrorModeChanged;
    public event EventHandler? CopyRequested;
    public event EventHandler? PasteRequested;
    public event EventHandler? PasteMirroredRequested;
    public event EventHandler? LinkRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? ParticleMoveEnded;
    public event EventHandler? ParticleMoveCanceled;

    private ParticlePreviewData? _data;
    private Point _lastMouse;
    private Point _mouseDown;
    private bool _rotating;
    private bool _panning;
    private bool _boxSelecting;
    private bool _movingParticles;
    private ParticleTransformMode _armedTransformMode = ParticleTransformMode.None;
    private ParticleTransformMode _activeTransformMode = ParticleTransformMode.None;
    private Vector3? _axisConstraint;
    private Keys _axisConstraintKey = Keys.None;
    private bool _localAxisConstraint;
    private bool _colliderRadiusScale;
    private Rectangle _selectionRectangle;
    private readonly HashSet<int> _selectedParticleIndices = new();
    private float _yaw = -0.55f;
    private float _pitch = 0.35f;
    private float _orthoHeight = 1.0f;
    private Vector3 _target = Vector3.Zero;
    private Vector3 _baseCenter = Vector3.Zero;
    private Vector3 _viewRoot = Vector3.Zero;
    private float _sceneRadius = 0.5f;
    private float _gridStep = 0.05f;
    private float _gridExtent = 1.0f;
    private bool _glReady;
    private bool _renderFailed;
    private string? _renderError;
    private int _program;
    private int _vao;
    private int _vbo;
    private int _uMvp;
    private int _uColor;
    private int _uPointSize;
    private int _uRoundPoints;
    private readonly ContextMenuStrip _viewportMenu;
    private bool _mirrorModeEnabled;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedParticleIndex { get; set; } = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedBoneIndex { get; set; } = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedColliderIndex { get; set; } = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PreviewPickKind PickKind { get; set; } = PreviewPickKind.Particle;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool MirrorModeEnabled
    {
        get => _mirrorModeEnabled;
        set
        {
            if (_mirrorModeEnabled == value)
                return;

            _mirrorModeEnabled = value;
            MirrorModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SysVec3 ViewRoot => new(_viewRoot.X, _viewRoot.Y, _viewRoot.Z);

    public ViewportCameraState CaptureCameraState()
    {
        return new ViewportCameraState
        {
            Yaw = _yaw,
            Pitch = _pitch,
            OrthoHeight = _orthoHeight,
            Target = new SysVec3(_target.X, _target.Y, _target.Z)
        };
    }

    public void RestoreCameraState(ViewportCameraState? state)
    {
        if (state == null)
            return;

        _yaw = state.Yaw;
        _pitch = state.Pitch;
        _orthoHeight = state.OrthoHeight;
        _target = new Vector3(state.Target.X, state.Target.Y, state.Target.Z);
        Invalidate();
    }

    public void SetSelectedParticleIndices(IEnumerable<int> indices)
    {
        _selectedParticleIndices.Clear();
        foreach (var index in indices)
            _selectedParticleIndices.Add(index);

        SelectedParticleIndex = _selectedParticleIndices.Count == 1 ? _selectedParticleIndices.First() : -1;
        Invalidate();
    }

    public ParticlePreviewControl()
        : base(new GLControlSettings())
    {
        BackColor = Color.FromArgb(56, 56, 56);
        TabStop = true;
        _viewportMenu = BuildViewportMenu();
    }

    public void SetData(ParticlePreviewData? data, bool resetCamera = true)
    {
        _data = data;
        if (resetCamera || data == null)
            ResetCameraToData();
        else
            UpdateSceneBounds(updateGrid: false);
        Invalidate();
    }

    public void UpdateParticlePreviewRows(IEnumerable<ParticleEditRow> rows)
    {
        if (_data == null)
            return;

        var previewRows = _data.Particles.ToDictionary(p => p.Index);
        foreach (var row in rows)
        {
            if (!previewRows.TryGetValue(row.Index, out var particle))
                continue;

            particle.Position = new SysVec3(row.X, row.Y, row.Z);
            particle.Fixed = row.Fixed;
            particle.Radius = row.Radius;
        }

        Invalidate();
    }

    // Bone edits happen repeatedly while a modal G/R/S transform is active.
    // Rebuilding the entire HKCL/BPHCL preview here made those transforms crawl,
    // so update the viewport's already-loaded skeleton instead.
    public void UpdateBonePreviewRows(IReadOnlyList<BoneEditRow> rows)
    {
        if (_data == null)
            return;

        var rowsByIndex = rows.ToDictionary(row => row.Index);
        var worldMatrices = new Dictionary<int, SysMat4>();
        foreach (var bone in _data.Bones)
        {
            if (!rowsByIndex.ContainsKey(bone.Index))
                continue;

            var world = GetBoneWorldMatrix(bone.Index, rowsByIndex, worldMatrices, new HashSet<int>());
            bone.Position = new SysVec3(world.M41, world.M42, world.M43);
            bone.AxisX = NormalizeOrDefault(SysVec3.TransformNormal(SysVec3.UnitX, world), SysVec3.UnitX);
            bone.AxisY = NormalizeOrDefault(SysVec3.TransformNormal(SysVec3.UnitY, world), SysVec3.UnitY);
            bone.AxisZ = NormalizeOrDefault(SysVec3.TransformNormal(SysVec3.UnitZ, world), SysVec3.UnitZ);
        }

        Invalidate();
    }

    public void UpdateColliderPreviewRows(IEnumerable<ColliderEditRow> rows)
    {
        if (_data == null)
            return;

        var previewRows = _data.Colliders.ToDictionary(collider => collider.Index);
        foreach (var row in rows)
        {
            if (!previewRows.TryGetValue(row.Index, out var collider))
                continue;

            collider.Start = new SysVec3(row.StartX, row.StartY, row.StartZ);
            collider.End = new SysVec3(row.EndX, row.EndY, row.EndZ);
            collider.Radius = row.Radius;
        }

        Invalidate();
    }

    private static SysMat4 GetBoneWorldMatrix(
        int boneIndex,
        IReadOnlyDictionary<int, BoneEditRow> rows,
        IDictionary<int, SysMat4> cache,
        ISet<int> visiting)
    {
        if (boneIndex < 0 || !rows.TryGetValue(boneIndex, out var bone))
            return SysMat4.Identity;
        if (cache.TryGetValue(boneIndex, out var cached))
            return cached;
        if (!visiting.Add(boneIndex))
            return SysMat4.Identity;

        var parentWorld = GetBoneWorldMatrix(bone.ParentIndex, rows, cache, visiting);
        visiting.Remove(boneIndex);

        var rotation = new SysQuat(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
        rotation = rotation.LengthSquared() < 0.000001f ? SysQuat.Identity : SysQuat.Normalize(rotation);
        var local = SysMat4.CreateScale(bone.ScaleX, bone.ScaleY, bone.ScaleZ)
            * SysMat4.CreateFromQuaternion(rotation)
            * SysMat4.CreateTranslation(bone.X, bone.Y, bone.Z);
        var world = local * parentWorld;
        cache[boneIndex] = world;
        return world;
    }

    private static SysVec3 NormalizeOrDefault(SysVec3 value, SysVec3 fallback)
    {
        return value.LengthSquared() < 0.000001f ? fallback : SysVec3.Normalize(value);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            MakeCurrent();
            GL.ClearColor(0.22f, 0.22f, 0.22f, 1.0f);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.ProgramPointSize);

            CreateShaderProgram();
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            _glReady = true;
        }
        catch (Exception ex)
        {
            _renderFailed = true;
            _renderError = ex.Message;
            _glReady = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_glReady)
        {
            MakeCurrent();
            if (_vbo != 0)
                GL.DeleteBuffer(_vbo);
            if (_vao != 0)
                GL.DeleteVertexArray(_vao);
            if (_program != 0)
                GL.DeleteProgram(_program);
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_activeTransformMode != ParticleTransformMode.None)
        {
            if (e.Button == MouseButtons.Left)
                FinishModalTransform(commit: true);
            else if (e.Button == MouseButtons.Right)
                FinishModalTransform(commit: false);
            return;
        }

        if (e.Button == MouseButtons.Left && TryHandleAxisWidgetClick(e.Location))
        {
            Focus();
            return;
        }

        _lastMouse = e.Location;
        _mouseDown = e.Location;
        var shiftDown = (ModifierKeys & Keys.Shift) == Keys.Shift;
        _rotating = e.Button == MouseButtons.Middle && !shiftDown;
        _panning = e.Button == MouseButtons.Middle && shiftDown;
        _boxSelecting = false;
        _movingParticles = false;

        if (e.Button == MouseButtons.Left)
        {
            _boxSelecting = true;
            _selectionRectangle = Rectangle.Empty;
        }

        Capture = _rotating || _panning || _boxSelecting || _movingParticles;
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_activeTransformMode != ParticleTransformMode.None && _armedTransformMode != ParticleTransformMode.None)
        {
            ApplyModalTransform(e.Location);
            _lastMouse = e.Location;
            Invalidate();
            return;
        }

        if (!_rotating && !_panning && !_boxSelecting && !_movingParticles)
            return;

        var dx = e.X - _lastMouse.X;
        var dy = e.Y - _lastMouse.Y;

        if (_rotating)
        {
            _yaw += dx * 0.01f;
            _pitch = Math.Clamp(_pitch + dy * 0.01f, -1.45f, 1.45f);
        }
        else if (_panning)
        {
            var (_, right, up) = GetCameraBasis();
            var worldPerPixel = _orthoHeight / Math.Max(1, ClientSize.Height);
            _target -= right * (dx * worldPerPixel);
            _target += up * (dy * worldPerPixel);
        }
        else if (_movingParticles)
        {
            var (_, right, up) = GetCameraBasis();
            var worldPerPixel = _orthoHeight / Math.Max(1, ClientSize.Height);
            if (_activeTransformMode == ParticleTransformMode.Scale)
            {
                var factor = MathF.Exp((dy - dx) * 0.01f);
                ParticlesScaled?.Invoke(this, new ParticleScaleEventArgs(factor, ToSysAxis(_axisConstraint), _localAxisConstraint, _colliderRadiusScale));
            }
            else if (_activeTransformMode == ParticleTransformMode.Rotate)
            {
                var axis = _axisConstraint ?? GetCameraBasis().Forward;
                ParticlesRotated?.Invoke(this, new ParticleRotateEventArgs(dx * 0.012f, ToSysVector(axis), _localAxisConstraint));
            }
            else
            {
                var delta = right * (dx * worldPerPixel) - up * (dy * worldPerPixel);
                if (_axisConstraint.HasValue)
                    delta = _axisConstraint.Value * Vector3.Dot(delta, _axisConstraint.Value);
                ParticlesMoved?.Invoke(this, new ParticleMoveEventArgs(ToSysVector(delta), _localAxisConstraint));
            }
        }
        else if (_boxSelecting)
        {
            _selectionRectangle = MakeRectangle(_mouseDown, e.Location);
        }

        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var wasMoving = _movingParticles;
        var wasBoxSelecting = _boxSelecting;
        var wasPanning = _panning;
        var moved = Math.Abs(e.X - _mouseDown.X) + Math.Abs(e.Y - _mouseDown.Y);
        _rotating = false;
        _panning = false;
        _boxSelecting = false;
        _movingParticles = false;
        if (wasMoving)
        {
            _activeTransformMode = ParticleTransformMode.None;
            _armedTransformMode = ParticleTransformMode.None;
            _axisConstraint = null;
            _axisConstraintKey = Keys.None;
            _localAxisConstraint = false;
        }
        Capture = false;

        if (wasMoving)
        {
            if (e.Button == MouseButtons.Right && moved <= 3)
            {
                ParticleMoveCanceled?.Invoke(this, EventArgs.Empty);
                _viewportMenu.Show(this, e.Location);
            }
            else
            {
                ParticleMoveEnded?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (wasBoxSelecting && e.Button == MouseButtons.Left)
        {
            if (moved <= 3)
                PickAt(e.Location);
            else
                SelectParticlesInRectangle(_selectionRectangle);
            _selectionRectangle = Rectangle.Empty;
        }
        else if (!wasPanning && e.Button == MouseButtons.Right && moved <= 3)
        {
            _viewportMenu.Show(this, e.Location);
        }
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _orthoHeight = Math.Clamp(_orthoHeight * (e.Delta > 0 ? 0.88f : 1.14f), 0.02f, 50.0f);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_activeTransformMode != ParticleTransformMode.None)
        {
            if (e.KeyCode == Keys.X)
            {
                SetAxisConstraint(Keys.X, Vector3.UnitX);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Y)
            {
                SetAxisConstraint(Keys.Y, Vector3.UnitY);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Z)
            {
                SetAxisConstraint(Keys.Z, Vector3.UnitZ);
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.S &&
                     PickKind == PreviewPickKind.Collider &&
                     _activeTransformMode == ParticleTransformMode.Scale)
            {
                _colliderRadiusScale = true;
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                FinishModalTransform(commit: false);
                e.Handled = true;
            }
            return;
        }

        if (e.KeyCode == Keys.F)
        {
            LinkRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (!HasTransformSelection())
            return;

        if (e.KeyCode == Keys.G)
        {
            StartModalTransform(ParticleTransformMode.Move);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.S)
        {
            StartModalTransform(ParticleTransformMode.Scale);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.R)
        {
            StartModalTransform(ParticleTransformMode.Rotate);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            _armedTransformMode = ParticleTransformMode.None;
            e.Handled = true;
        }
    }

    private void StartModalTransform(ParticleTransformMode mode)
    {
        _armedTransformMode = mode;
        _activeTransformMode = mode;
        _axisConstraint = null;
        _axisConstraintKey = Keys.None;
        _localAxisConstraint = false;
        _colliderRadiusScale = false;
        _lastMouse = PointToClient(Cursor.Position);
        _mouseDown = _lastMouse;
        _rotating = false;
        _panning = false;
        _boxSelecting = false;
        _movingParticles = false;
        Capture = true;
        ParticleMoveStarted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyModalTransform(Point location)
    {
        var dx = location.X - _lastMouse.X;
        var dy = location.Y - _lastMouse.Y;
        if (dx == 0 && dy == 0)
            return;

        var worldPerPixel = _orthoHeight / Math.Max(1, ClientSize.Height);
        if (_activeTransformMode == ParticleTransformMode.Scale)
        {
            var factor = MathF.Exp((dy - dx) * 0.01f);
            ParticlesScaled?.Invoke(this, new ParticleScaleEventArgs(factor, ToSysAxis(_axisConstraint), _localAxisConstraint, _colliderRadiusScale));
        }
        else if (_activeTransformMode == ParticleTransformMode.Rotate)
        {
            var axis = _axisConstraint ?? GetCameraBasis().Forward;
            ParticlesRotated?.Invoke(this, new ParticleRotateEventArgs(dx * 0.012f, ToSysVector(axis), _localAxisConstraint));
        }
        else
        {
            var (_, right, up) = GetCameraBasis();
            var delta = right * (dx * worldPerPixel) - up * (dy * worldPerPixel);
            if (_axisConstraint.HasValue)
                delta = _axisConstraint.Value * Vector3.Dot(delta, _axisConstraint.Value);
            ParticlesMoved?.Invoke(this, new ParticleMoveEventArgs(ToSysVector(delta), _localAxisConstraint));
        }
    }

    private void FinishModalTransform(bool commit)
    {
        if (_activeTransformMode == ParticleTransformMode.None)
            return;

        _activeTransformMode = ParticleTransformMode.None;
        _armedTransformMode = ParticleTransformMode.None;
        _axisConstraint = null;
        _axisConstraintKey = Keys.None;
        _localAxisConstraint = false;
        _colliderRadiusScale = false;
        Capture = false;
        if (commit)
            ParticleMoveEnded?.Invoke(this, EventArgs.Empty);
        else
            ParticleMoveCanceled?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private ContextMenuStrip BuildViewportMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(48, 48, 48),
            ForeColor = Color.Gainsboro,
            Renderer = new DarkToolStripRenderer()
        };
        var global = new ToolStripMenuItem("Mirror Global");
        StyleMenuItem(global);
        global.DropDownItems.Add(MakeMirrorMenuItem("X", 'X', local: false));
        global.DropDownItems.Add(MakeMirrorMenuItem("Y", 'Y', local: false));
        global.DropDownItems.Add(MakeMirrorMenuItem("Z", 'Z', local: false));

        var local = new ToolStripMenuItem("Mirror Local");
        StyleMenuItem(local);
        local.DropDownItems.Add(MakeMirrorMenuItem("X", 'X', local: true));
        local.DropDownItems.Add(MakeMirrorMenuItem("Y", 'Y', local: true));
        local.DropDownItems.Add(MakeMirrorMenuItem("Z", 'Z', local: true));

        var delete = new ToolStripMenuItem("Delete Selected");
        StyleMenuItem(delete);
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        var copy = new ToolStripMenuItem("Copy");
        StyleMenuItem(copy);
        copy.Click += (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty);

        var paste = new ToolStripMenuItem("Paste");
        StyleMenuItem(paste);
        paste.Click += (_, _) => PasteRequested?.Invoke(this, EventArgs.Empty);

        var pasteMirrored = new ToolStripMenuItem("Paste X-Flipped");
        StyleMenuItem(pasteMirrored);
        pasteMirrored.Click += (_, _) => PasteMirroredRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(global);
        menu.Items.Add(local);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(pasteMirrored);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(delete);
        return menu;
    }

    private ToolStripMenuItem MakeMirrorMenuItem(string text, char axis, bool local)
    {
        var item = new ToolStripMenuItem(text);
        StyleMenuItem(item);
        item.Click += (_, _) => MirrorRequested?.Invoke(this, new MirrorRequestedEventArgs(axis, local));
        return item;
    }

    private static void StyleMenuItem(ToolStripMenuItem item)
    {
        item.BackColor = Color.FromArgb(48, 48, 48);
        item.ForeColor = Color.Gainsboro;
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable())
        {
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientBegin => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientEnd => Color.FromArgb(48, 48, 48);
        public override Color MenuItemSelected => Color.FromArgb(70, 70, 70);
        public override Color MenuItemBorder => Color.FromArgb(110, 110, 110);
        public override Color MenuBorder => Color.FromArgb(90, 90, 90);
    }

    private bool TryHandleAxisWidgetClick(Point location)
    {
        var reset = GetAxisResetRectangle();
        if (reset.Contains(location))
        {
            ResetCameraToData();
            Invalidate();
            return true;
        }

        var points = GetAxisWidgetPoints();
        foreach (var item in points)
        {
            if (DistanceSquared(item.Screen, location) > 14.0f * 14.0f)
                continue;

            SetCameraForward(-item.Axis);
            Invalidate();
            return true;
        }

        return false;
    }

    private Rectangle GetAxisResetRectangle()
    {
        return new Rectangle(Math.Max(8, Width - 138), 12, 34, 24);
    }

    private IReadOnlyList<(Vector3 Axis, PointF Screen, Vector4 Color)> GetAxisWidgetPoints()
    {
        var center = new PointF(Width - 58, 58);
        var size = 36.0f;
        var (_, right, up) = GetCameraBasis();
        return new[]
        {
            MakeAxisPoint(Vector3.UnitX, center, size, right, up, new Vector4(1.0f, 0.16f, 0.16f, 1.0f)),
            MakeAxisPoint(-Vector3.UnitX, center, size, right, up, new Vector4(0.65f, 0.10f, 0.10f, 1.0f)),
            MakeAxisPoint(Vector3.UnitY, center, size, right, up, new Vector4(0.20f, 0.95f, 0.34f, 1.0f)),
            MakeAxisPoint(-Vector3.UnitY, center, size, right, up, new Vector4(0.10f, 0.55f, 0.20f, 1.0f)),
            MakeAxisPoint(Vector3.UnitZ, center, size, right, up, new Vector4(0.25f, 0.42f, 1.0f, 1.0f)),
            MakeAxisPoint(-Vector3.UnitZ, center, size, right, up, new Vector4(0.12f, 0.22f, 0.65f, 1.0f))
        };
    }

    private static (Vector3 Axis, PointF Screen, Vector4 Color) MakeAxisPoint(Vector3 axis, PointF center, float size, Vector3 right, Vector3 up, Vector4 color)
    {
        var x = Vector3.Dot(axis, right);
        var y = -Vector3.Dot(axis, up);
        return (axis, new PointF(center.X + x * size, center.Y + y * size), color);
    }

    private void SetCameraForward(Vector3 forward)
    {
        if (forward.LengthSquared < 0.000001f)
            return;

        forward.Normalize();
        _pitch = MathF.Asin(Math.Clamp(forward.Y, -1.0f, 1.0f));
        var cosPitch = MathF.Max(0.0001f, MathF.Cos(_pitch));
        _yaw = MathF.Atan2(forward.X / cosPitch, forward.Z / cosPitch);
    }

    private void DrawAxisWidget()
    {
        if (Width <= 0 || Height <= 0)
            return;

        var points = GetAxisWidgetPoints();
        var center = new PointF(Width - 58, 58);
        var centerNdc = ScreenToNdc(center);
        foreach (var item in points)
        {
            DrawSingleColor(
                PrimitiveType.Lines,
                new[] { centerNdc, ScreenToNdc(item.Screen) },
                item.Color,
                Matrix4.Identity,
                2.4f,
                1.0f,
                false);
            DrawSingleColor(
                PrimitiveType.Points,
                new[] { ScreenToNdc(item.Screen) },
                item.Color,
                Matrix4.Identity,
                1.0f,
                8.0f,
                false);
        }

        var reset = GetAxisResetRectangle();
        var resetLines = new[]
        {
            ScreenToNdc(new PointF(reset.Left, reset.Top)), ScreenToNdc(new PointF(reset.Right, reset.Top)),
            ScreenToNdc(new PointF(reset.Right, reset.Top)), ScreenToNdc(new PointF(reset.Right, reset.Bottom)),
            ScreenToNdc(new PointF(reset.Right, reset.Bottom)), ScreenToNdc(new PointF(reset.Left, reset.Bottom)),
            ScreenToNdc(new PointF(reset.Left, reset.Bottom)), ScreenToNdc(new PointF(reset.Left, reset.Top)),
            ScreenToNdc(new PointF(reset.Left + 9, reset.Top + 12)), ScreenToNdc(new PointF(reset.Right - 9, reset.Top + 12)),
            ScreenToNdc(new PointF(reset.Left + 17, reset.Top + 6)), ScreenToNdc(new PointF(reset.Left + 9, reset.Top + 12)),
            ScreenToNdc(new PointF(reset.Left + 17, reset.Bottom - 6)), ScreenToNdc(new PointF(reset.Left + 9, reset.Top + 12))
        };
        DrawSingleColor(PrimitiveType.Lines, resetLines, new Vector4(0.92f, 0.92f, 0.92f, 1.0f), Matrix4.Identity, 1.6f, 1.0f, false);
    }

    private Vector3 ScreenToNdc(PointF point)
    {
        return new Vector3(
            (point.X / Math.Max(1.0f, Width)) * 2.0f - 1.0f,
            1.0f - (point.Y / Math.Max(1.0f, Height)) * 2.0f,
            0.0f);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_renderFailed)
        {
            DrawFallbackMessage(e.Graphics, "OpenGL viewport failed to initialize.", _renderError);
            return;
        }

        if (!_glReady)
        {
            base.OnPaint(e);
            return;
        }

        try
        {
            MakeCurrent();
            GL.Viewport(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var mvp = GetMvp();
            DrawGridAndAxes(mvp);
            DrawColliders(mvp);

            if (_data != null && (_data.Particles.Count > 0 || _data.Bones.Count > 0))
            {
                DrawTriangles(mvp);
                DrawSkeleton(mvp);
                DrawLinks(mvp);
                DrawParticles(mvp);
                DrawTransformAxis(mvp);
            }

            DrawSelectionRectangle();
            DrawAxisWidget();
            SwapBuffers();
        }
        catch (Exception ex)
        {
            _renderFailed = true;
            _renderError = ex.Message;
            DrawFallbackMessage(e.Graphics, "OpenGL viewport render failed.", _renderError);
        }
    }

    private void ResetCameraToData()
    {
        if (_data == null)
        {
            _baseCenter = Vector3.Zero;
            _viewRoot = Vector3.Zero;
            _target = Vector3.Zero;
            _yaw = -0.55f;
            _pitch = 0.35f;
            _sceneRadius = 0.5f;
            _orthoHeight = 1.0f;
            return;
        }

        UpdateSceneBounds();
        _target = _viewRoot;
        _yaw = -0.55f;
        _pitch = 0.35f;
        _orthoHeight = Math.Max(0.2f, _sceneRadius * 2.4f);
    }

    private void UpdateSceneBounds(bool updateGrid = true)
    {
        if (_data == null)
            return;

        var previousRadius = _sceneRadius;
        var points = _data.Particles.Select(p => ToGl(p.Position))
            .Concat(_data.Bones.Select(b => ToGl(b.Position)))
            .Concat(_data.Colliders.Select(c => ToGl(c.Start)))
            .Concat(_data.Colliders.Select(c => ToGl(c.End)))
            .Where(IsFinite)
            .ToList();

        if (points.Count == 0)
            return;

        _baseCenter = new Vector3(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        // The viewport has its own fixed world origin. It must not follow a
        // skeleton's Root bone, otherwise moving that bone drags the grid and
        // axes along with the armature.
        _viewRoot = Vector3.Zero;
        _sceneRadius = Math.Max(0.05f, points.Max(p => (p - _viewRoot).Length));
        if (updateGrid)
        {
            _gridStep = FixedGridStep(_sceneRadius) * 4.0f;
            _gridExtent = Math.Max(_sceneRadius * 3.5f, _gridStep * 20.0f);
        }

        // A large manual coordinate edit should remain visible instead of
        // leaving the camera framed around the old, much smaller scene.
        if (_sceneRadius > previousRadius * 1.5f)
            _orthoHeight = Math.Max(_orthoHeight, _sceneRadius * 2.4f);
    }

    private void PickAt(Point location)
    {
        if (_data == null || Width <= 0 || Height <= 0)
            return;

        var mvp = GetMvp();
        var bestDistance = 14.0f * 14.0f;
        PreviewPickKind? bestKind = null;
        var bestIndex = -1;

        if (PickKind == PreviewPickKind.Particle)
        {
            foreach (var particle in _data.Particles)
                ConsiderPoint(PreviewPickKind.Particle, particle.Index, ToGl(particle.Position), location, mvp, ref bestDistance, ref bestKind, ref bestIndex);
        }
        else if (PickKind == PreviewPickKind.Bone)
        {
            foreach (var bone in _data.Bones)
                ConsiderPoint(PreviewPickKind.Bone, bone.Index, ToGl(bone.Position), location, mvp, ref bestDistance, ref bestKind, ref bestIndex);
        }
        else if (PickKind == PreviewPickKind.Collider)
        {
            foreach (var collider in _data.Colliders)
                ConsiderSegment(PreviewPickKind.Collider, collider.Index, ToGl(collider.Start), ToGl(collider.End), location, mvp, ref bestDistance, ref bestKind, ref bestIndex);
        }

        var addToSelection = (ModifierKeys & Keys.Shift) == Keys.Shift || (ModifierKeys & Keys.Control) == Keys.Control;
        if (bestKind.HasValue)
            ItemPicked?.Invoke(this, new PreviewPickEventArgs(bestKind.Value, bestIndex, addToSelection));
        else
            ItemPicked?.Invoke(this, new PreviewPickEventArgs(PickKind, -1, addToSelection));
    }

    private bool TryPickParticleAt(Point location, out int particleIndex)
    {
        particleIndex = -1;
        if (_data == null || Width <= 0 || Height <= 0)
            return false;

        var mvp = GetMvp();
        var bestDistance = 14.0f * 14.0f;
        foreach (var particle in _data.Particles)
        {
            if (!TryProject(ToGl(particle.Position), mvp, out var screen))
                continue;

            var distance = DistanceSquared(screen, location);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            particleIndex = particle.Index;
        }

        return particleIndex >= 0;
    }

    private void SelectParticlesInRectangle(Rectangle rectangle)
    {
        if (_data == null || rectangle.Width <= 2 || rectangle.Height <= 2)
        {
            ParticlesSelected?.Invoke(this, new ParticleSelectionEventArgs(Array.Empty<int>(), (ModifierKeys & Keys.Shift) == Keys.Shift || (ModifierKeys & Keys.Control) == Keys.Control));
            return;
        }

        var mvp = GetMvp();
        var selected = new List<int>();
        foreach (var particle in _data.Particles)
        {
            if (TryProject(ToGl(particle.Position), mvp, out var screen) && rectangle.Contains(Point.Round(screen)))
                selected.Add(particle.Index);
        }

        ParticlesSelected?.Invoke(this, new ParticleSelectionEventArgs(selected, (ModifierKeys & Keys.Shift) == Keys.Shift || (ModifierKeys & Keys.Control) == Keys.Control));
    }

    private void ConsiderPoint(
        PreviewPickKind kind,
        int index,
        Vector3 world,
        Point mouse,
        Matrix4 mvp,
        ref float bestDistance,
        ref PreviewPickKind? bestKind,
        ref int bestIndex)
    {
        if (!TryProject(world, mvp, out var screen))
            return;

        var distance = DistanceSquared(screen, mouse);
        if (distance >= bestDistance)
            return;

        bestDistance = distance;
        bestKind = kind;
        bestIndex = index;
    }

    private void ConsiderSegment(
        PreviewPickKind kind,
        int index,
        Vector3 start,
        Vector3 end,
        Point mouse,
        Matrix4 mvp,
        ref float bestDistance,
        ref PreviewPickKind? bestKind,
        ref int bestIndex)
    {
        if (!TryProject(start, mvp, out var a) || !TryProject(end, mvp, out var b))
            return;

        var distance = DistanceToSegmentSquared(mouse, a, b);
        if (distance >= bestDistance)
            return;

        bestDistance = distance;
        bestKind = kind;
        bestIndex = index;
    }

    private bool TryProject(Vector3 world, Matrix4 mvp, out PointF screen)
    {
        var clip = Vector4.TransformRow(new Vector4(world, 1.0f), mvp);
        if (MathF.Abs(clip.W) < 0.000001f)
        {
            screen = PointF.Empty;
            return false;
        }

        var ndcX = clip.X / clip.W;
        var ndcY = clip.Y / clip.W;
        screen = new PointF(
            (ndcX * 0.5f + 0.5f) * Width,
            (1.0f - (ndcY * 0.5f + 0.5f)) * Height);
        return ndcX >= -1.2f && ndcX <= 1.2f && ndcY >= -1.2f && ndcY <= 1.2f;
    }

    private void DrawSelectionRectangle()
    {
        if (!_boxSelecting || _selectionRectangle.Width <= 2 || _selectionRectangle.Height <= 2)
            return;

        var left = (_selectionRectangle.Left / Math.Max(1.0f, Width)) * 2.0f - 1.0f;
        var right = (_selectionRectangle.Right / Math.Max(1.0f, Width)) * 2.0f - 1.0f;
        var top = 1.0f - (_selectionRectangle.Top / Math.Max(1.0f, Height)) * 2.0f;
        var bottom = 1.0f - (_selectionRectangle.Bottom / Math.Max(1.0f, Height)) * 2.0f;
        var vertices = new[]
        {
            new Vector3(left, top, 0.0f),
            new Vector3(right, top, 0.0f),
            new Vector3(right, top, 0.0f),
            new Vector3(right, bottom, 0.0f),
            new Vector3(right, bottom, 0.0f),
            new Vector3(left, bottom, 0.0f),
            new Vector3(left, bottom, 0.0f),
            new Vector3(left, top, 0.0f)
        };
        var identity = Matrix4.Identity;
        DrawSingleColor(PrimitiveType.Lines, vertices, new Vector4(1.0f, 1.0f, 1.0f, 0.92f), identity, 1.5f, 1.0f, false);
    }

    private static float DistanceSquared(PointF a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static float DistanceToSegmentSquared(Point p, PointF a, PointF b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var wx = p.X - a.X;
        var wy = p.Y - a.Y;
        var lengthSq = vx * vx + vy * vy;
        var t = lengthSq < 0.000001f ? 0.0f : Math.Clamp((wx * vx + wy * vy) / lengthSq, 0.0f, 1.0f);
        var x = a.X + vx * t;
        var y = a.Y + vy * t;
        var dx = p.X - x;
        var dy = p.Y - y;
        return dx * dx + dy * dy;
    }

    private static Rectangle MakeRectangle(Point a, Point b)
    {
        return new Rectangle(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X),
            Math.Abs(a.Y - b.Y));
    }

    private static SysVec3? ToSysAxis(Vector3? axis)
    {
        return axis.HasValue ? ToSysVector(axis.Value) : null;
    }

    private static SysVec3 ToSysVector(Vector3 vector) => new(-vector.X, vector.Y, vector.Z);

    private Matrix4 GetMvp()
    {
        var aspect = Math.Max(1.0f, Width) / Math.Max(1.0f, Height);
        var eyeDistance = Math.Max(3.0f, _sceneRadius * 5.0f);
        var clipRange = Math.Max(100.0f, eyeDistance + _sceneRadius * 4.0f + 10.0f);
        var projection = Matrix4.CreateOrthographic(_orthoHeight * aspect, _orthoHeight, -clipRange, clipRange);
        var (forward, _, up) = GetCameraBasis();
        var eye = _target - forward * eyeDistance;
        var view = Matrix4.LookAt(eye, _target, up);
        return view * projection;
    }

    private (Vector3 Forward, Vector3 Right, Vector3 Up) GetCameraBasis()
    {
        var forward = new Vector3(
            MathF.Sin(_yaw) * MathF.Cos(_pitch),
            MathF.Sin(_pitch),
            MathF.Cos(_yaw) * MathF.Cos(_pitch));
        forward.Normalize();

        var right = Vector3.Cross(forward, Vector3.UnitY);
        if (right.LengthSquared < 0.000001f)
            right = Vector3.UnitX;
        else
            right.Normalize();

        var up = Vector3.Cross(right, forward);
        up.Normalize();
        return (forward, right, up);
    }

    private void DrawGridAndAxes(Matrix4 mvp)
    {
        var gridVertices = new List<Vector3>();
        var gridY = _viewRoot.Y;
        var step = _gridStep;
        var half = Math.Clamp((int)MathF.Ceiling(_gridExtent / step), 8, 16);
        var centerX = _viewRoot.X;
        var centerZ = _viewRoot.Z;

        for (var i = -half; i <= half; i++)
        {
            var offset = i * step;
            AddLine(gridVertices, new Vector3(centerX - half * step, gridY, centerZ + offset), new Vector3(centerX + half * step, gridY, centerZ + offset));
            AddLine(gridVertices, new Vector3(centerX + offset, gridY, centerZ - half * step), new Vector3(centerX + offset, gridY, centerZ + half * step));
        }
        DrawSingleColor(PrimitiveType.Lines, gridVertices, new Vector4(0.62f, 0.62f, 0.62f, 0.50f), mvp, 1.0f, 1.0f, true);

        var axisExtent = half * step;
        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { new Vector3(centerX - axisExtent, gridY, centerZ), new Vector3(centerX + axisExtent, gridY, centerZ) },
            new Vector4(0.90f, 0.18f, 0.18f, 1.0f),
            mvp,
            2.2f,
            1.0f,
            false);
        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { new Vector3(centerX, gridY, centerZ - axisExtent), new Vector3(centerX, gridY, centerZ + axisExtent) },
            new Vector4(0.20f, 0.34f, 0.95f, 1.0f),
            mvp,
            2.2f,
            1.0f,
            false);
        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { new Vector3(centerX, gridY - axisExtent * 0.55f, centerZ), new Vector3(centerX, gridY + axisExtent * 0.55f, centerZ) },
            new Vector4(0.16f, 0.78f, 0.30f, 1.0f),
            mvp,
            2.2f,
            1.0f,
            false);
    }

    private void DrawSkeleton(Matrix4 mvp)
    {
        if (_data == null || _data.Bones.Count == 0)
            return;

        var lines = new List<Vector3>();
        var selectedLines = new List<Vector3>();
        var (_, cameraRight, cameraUp) = GetCameraBasis();
        foreach (var bone in _data.Bones)
        {
            var point = ToGl(bone.Position);
            var jointLines = bone.Index == SelectedBoneIndex ? selectedLines : lines;
            AddJointDiamond(jointLines, point, cameraRight, cameraUp);

            if (bone.ParentIndex < 0)
                continue;

            var parent = _data.Bones.FirstOrDefault(b => b.Index == bone.ParentIndex);
            if (parent == null)
                continue;

            var handleLines = bone.Index == SelectedBoneIndex || parent.Index == SelectedBoneIndex ? selectedLines : lines;
            AddBoneHandle(handleLines, ToGl(parent.Position), point);
        }

        if (lines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, lines, new Vector4(1.0f, 0.92f, 0.0f, 1.0f), mvp, 2.0f, 1.0f, false);
        if (selectedLines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, selectedLines, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), mvp, 3.0f, 1.0f, false);
    }

    private static void AddBoneHandle(List<Vector3> lines, Vector3 parent, Vector3 child)
    {
        var delta = child - parent;
        if (delta.LengthSquared < 0.000001f)
            return;

        AddLine(lines, parent, child);
    }

    private void AddJointDiamond(List<Vector3> lines, Vector3 point, Vector3 cameraRight, Vector3 cameraUp)
    {
        var size = Math.Max(0.004f, _sceneRadius * 0.006f);
        var left = point - cameraRight * size;
        var right = point + cameraRight * size;
        var top = point + cameraUp * size;
        var bottom = point - cameraUp * size;
        AddLine(lines, left, top);
        AddLine(lines, top, right);
        AddLine(lines, right, bottom);
        AddLine(lines, bottom, left);
    }

    private void DrawTriangles(Matrix4 mvp)
    {
        if (_data == null)
            return;

        var vertices = new List<Vector3>();
        foreach (var triangle in _data.Triangles)
        {
            if (!TryGetParticle(triangle.ParticleA, out var a)
                || !TryGetParticle(triangle.ParticleB, out var b)
                || !TryGetParticle(triangle.ParticleC, out var c))
                continue;

            vertices.Add(ToGl(a.Position));
            vertices.Add(ToGl(b.Position));
            vertices.Add(ToGl(c.Position));
        }

        if (vertices.Count > 0)
            DrawSingleColor(PrimitiveType.Triangles, vertices, new Vector4(0.40f, 0.62f, 0.95f, 0.22f), mvp, 1.0f, 1.0f, true);
    }

    private void DrawColliders(Matrix4 mvp)
    {
        if (_data == null || _data.Colliders.Count == 0)
            return;

        var lines = new List<Vector3>();
        var selectedLines = new List<Vector3>();
        foreach (var collider in _data.Colliders)
        {
            var target = collider.Index == SelectedColliderIndex ? selectedLines : lines;
            switch (collider.Kind)
            {
                case ColliderPreviewKind.Sphere:
                    AddSphereGuide(target, ToGl(collider.Start), collider.Radius);
                    break;
                case ColliderPreviewKind.TaperedCapsule:
                    AddTaperedCapsuleGuide(target, ToGl(collider.Start), ToGl(collider.End), collider.Radius, collider.EndRadius);
                    break;
                case ColliderPreviewKind.Plane:
                    AddPlaneGuide(target, ToGl(collider.Start), ToGl(collider.PlaneNormal), Math.Max(_sceneRadius * 0.18f, _gridStep * 2.0f));
                    break;
                case ColliderPreviewKind.Point:
                    AddSphereGuide(target, ToGl(collider.Start), Math.Max(_sceneRadius * 0.015f, _gridStep * 0.15f));
                    break;
                default:
                    AddCapsuleGuide(target, ToGl(collider.Start), ToGl(collider.End), collider.Radius);
                    break;
            }
        }

        if (lines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, lines, new Vector4(0.0f, 0.72f, 0.90f, 0.78f), mvp, 1.4f, 1.0f, false);
        if (selectedLines.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, selectedLines, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), mvp, 4.0f, 1.0f, false);
    }

    private static void AddCapsuleGuide(List<Vector3> lines, Vector3 start, Vector3 end, float radius)
    {
        AddLine(lines, start, end);
        var direction = end - start;
        if (direction.LengthSquared < 0.000001f)
            direction = Vector3.UnitY;
        else
            direction.Normalize();

        var side = Vector3.Cross(direction, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f)
            side = Vector3.Cross(direction, Vector3.UnitX);
        side.Normalize();
        var up = Vector3.Cross(side, direction);
        up.Normalize();
        var r = Math.Max(radius, 0.005f);

        AddCircle(lines, start, side, up, r);
        AddCircle(lines, end, side, up, r);
        AddLine(lines, start + side * r, end + side * r);
        AddLine(lines, start - side * r, end - side * r);
        AddLine(lines, start + up * r, end + up * r);
        AddLine(lines, start - up * r, end - up * r);
    }

    private static void AddTaperedCapsuleGuide(List<Vector3> lines, Vector3 start, Vector3 end, float startRadius, float endRadius)
    {
        var direction = end - start;
        if (direction.LengthSquared < 0.000001f)
        {
            AddSphereGuide(lines, start, Math.Max(startRadius, endRadius));
            return;
        }

        direction.Normalize();
        var side = Vector3.Cross(direction, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f)
            side = Vector3.Cross(direction, Vector3.UnitX);
        side.Normalize();
        var up = Vector3.Normalize(Vector3.Cross(side, direction));
        var a = Math.Max(startRadius, 0.005f);
        var b = Math.Max(endRadius, 0.005f);
        AddCircle(lines, start, side, up, a);
        AddCircle(lines, end, side, up, b);
        AddLine(lines, start + side * a, end + side * b);
        AddLine(lines, start - side * a, end - side * b);
        AddLine(lines, start + up * a, end + up * b);
        AddLine(lines, start - up * a, end - up * b);
    }

    private static void AddSphereGuide(List<Vector3> lines, Vector3 center, float radius)
    {
        var r = Math.Max(radius, 0.005f);
        AddCircle(lines, center, Vector3.UnitX, Vector3.UnitY, r);
        AddCircle(lines, center, Vector3.UnitX, Vector3.UnitZ, r);
        AddCircle(lines, center, Vector3.UnitY, Vector3.UnitZ, r);
    }

    private static void AddPlaneGuide(List<Vector3> lines, Vector3 center, Vector3 normal, float size)
    {
        if (normal.LengthSquared < 0.000001f)
            normal = Vector3.UnitY;
        else
            normal.Normalize();
        var side = Vector3.Cross(normal, Vector3.UnitY);
        if (side.LengthSquared < 0.000001f)
            side = Vector3.Cross(normal, Vector3.UnitX);
        side.Normalize();
        var up = Vector3.Normalize(Vector3.Cross(side, normal));
        var a = center - side * size - up * size;
        var b = center + side * size - up * size;
        var c = center + side * size + up * size;
        var d = center - side * size + up * size;
        AddLine(lines, a, b);
        AddLine(lines, b, c);
        AddLine(lines, c, d);
        AddLine(lines, d, a);
        AddLine(lines, center, center + normal * size * 0.6f);
    }

    private static void AddCircle(List<Vector3> lines, Vector3 center, Vector3 side, Vector3 up, float radius)
    {
        const int steps = 16;
        for (var i = 0; i < steps; i++)
        {
            var a = MathHelper.TwoPi * i / steps;
            var b = MathHelper.TwoPi * (i + 1) / steps;
            AddLine(
                lines,
                center + side * (MathF.Cos(a) * radius) + up * (MathF.Sin(a) * radius),
                center + side * (MathF.Cos(b) * radius) + up * (MathF.Sin(b) * radius));
        }
    }

    private void DrawLinks(Matrix4 mvp)
    {
        if (_data == null)
            return;

        var normal = new List<Vector3>();
        var selected = new List<Vector3>();
        foreach (var link in _data.Links)
        {
            if (!TryGetParticle(link.ParticleA, out var a) || !TryGetParticle(link.ParticleB, out var b))
                continue;

            var list = link.ParticleA == SelectedParticleIndex
                || link.ParticleB == SelectedParticleIndex
                || _selectedParticleIndices.Contains(link.ParticleA)
                || _selectedParticleIndices.Contains(link.ParticleB)
                ? selected
                : normal;
            AddLine(list, ToGl(a.Position), ToGl(b.Position));
        }

        if (normal.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, normal, new Vector4(0.45f, 0.45f, 0.45f, 1.0f), mvp, 1.8f, 1.0f, false);
        if (selected.Count > 0)
            DrawSingleColor(PrimitiveType.Lines, selected, new Vector4(1.0f, 0.74f, 0.18f, 1.0f), mvp, 3.0f, 1.0f, false);
    }

    private void DrawTransformAxis(Matrix4 mvp)
    {
        if (_data == null || _activeTransformMode == ParticleTransformMode.None || !_axisConstraint.HasValue)
            return;

        var selected = GetTransformSelectionPositions();
        if (selected.Count == 0)
            return;

        var center = new Vector3(
            selected.Average(p => p.X),
            selected.Average(p => p.Y),
            selected.Average(p => p.Z));
        var axis = _axisConstraint.Value;
        if (axis.LengthSquared < 0.000001f)
            axis = Vector3.UnitX;
        else
            axis.Normalize();

        var length = Math.Max(_sceneRadius * 0.45f, _gridStep * 3.0f);
        var color = _axisConstraint switch
        {
            { } a when MathF.Abs(Vector3.Dot(a, Vector3.UnitX)) > 0.9f => new Vector4(1.0f, 0.18f, 0.18f, 1.0f),
            { } a when MathF.Abs(Vector3.Dot(a, Vector3.UnitY)) > 0.9f => new Vector4(0.25f, 0.90f, 0.35f, 1.0f),
            { } a when MathF.Abs(Vector3.Dot(a, Vector3.UnitZ)) > 0.9f => new Vector4(0.25f, 0.42f, 1.0f, 1.0f),
            _ => new Vector4(1.0f, 0.86f, 0.18f, 1.0f)
        };

        DrawSingleColor(
            PrimitiveType.Lines,
            new[] { center - axis * length, center + axis * length },
            color,
            mvp,
            3.0f,
            1.0f,
            false);
    }

    private void DrawParticles(Matrix4 mvp)
    {
        if (_data == null)
            return;

        var fixedParticles = new List<Vector3>();
        var dynamicParticles = new List<Vector3>();
        var selectedParticles = new List<Vector3>();

        foreach (var particle in _data.Particles)
        {
            if (_selectedParticleIndices.Contains(particle.Index) || particle.Index == SelectedParticleIndex)
                selectedParticles.Add(ToGl(particle.Position));
            else if (particle.Fixed)
                fixedParticles.Add(ToGl(particle.Position));
            else
                dynamicParticles.Add(ToGl(particle.Position));
        }

        if (dynamicParticles.Count > 0)
            DrawSingleColor(PrimitiveType.Points, dynamicParticles, new Vector4(0.20f, 0.48f, 0.92f, 1.0f), mvp, 1.0f, 9.0f, false);
        if (fixedParticles.Count > 0)
            DrawSingleColor(PrimitiveType.Points, fixedParticles, new Vector4(0.88f, 0.18f, 0.18f, 1.0f), mvp, 1.0f, 10.0f, false);
        if (selectedParticles.Count > 0)
            DrawSingleColor(PrimitiveType.Points, selectedParticles, new Vector4(1.0f, 0.80f, 0.18f, 1.0f), mvp, 1.0f, 13.0f, false);
    }

    private void DrawSingleColor(
        PrimitiveType primitive,
        IReadOnlyList<Vector3> vertices,
        Vector4 color,
        Matrix4 mvp,
        float lineWidth,
        float pointSize,
        bool depthTest)
    {
        DrawSegments(primitive, vertices, new[] { (0, vertices.Count, color) }, mvp, lineWidth, pointSize, depthTest);
    }

    private void DrawSegments(
        PrimitiveType primitive,
        IReadOnlyList<Vector3> vertices,
        IEnumerable<(int Start, int Count, Vector4 Color)> segments,
        Matrix4 mvp,
        float lineWidth,
        float pointSize,
        bool depthTest)
    {
        if (vertices.Count == 0)
            return;

        if (depthTest)
            GL.Enable(EnableCap.DepthTest);
        else
            GL.Disable(EnableCap.DepthTest);

        GL.LineWidth(lineWidth);
        GL.UseProgram(_program);
        GL.UniformMatrix4(_uMvp, false, ref mvp);
        GL.Uniform1(_uPointSize, pointSize);
        GL.Uniform1(_uRoundPoints, primitive == PrimitiveType.Points ? 1 : 0);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * 3 * sizeof(float), ToFloatArray(vertices), BufferUsageHint.DynamicDraw);

        foreach (var segment in segments)
        {
            GL.Uniform4(_uColor, segment.Color);
            GL.DrawArrays(primitive, segment.Start, segment.Count);
        }

        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    private void CreateShaderProgram()
    {
        const string vertexSource = """
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            uniform mat4 uMvp;
            uniform float uPointSize;
            void main()
            {
                gl_Position = uMvp * vec4(aPosition, 1.0);
                gl_PointSize = uPointSize;
            }
            """;

        const string fragmentSource = """
            #version 330 core
            uniform vec4 uColor;
            uniform int uRoundPoints;
            out vec4 FragColor;
            void main()
            {
                if (uRoundPoints != 0)
                {
                    vec2 point = gl_PointCoord * 2.0 - 1.0;
                    if (dot(point, point) > 1.0)
                        discard;
                }
                FragColor = uColor;
            }
            """;

        var vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        _program = GL.CreateProgram();
        GL.AttachShader(_program, vertex);
        GL.AttachShader(_program, fragment);
        GL.LinkProgram(_program);
        GL.GetProgram(_program, GetProgramParameterName.LinkStatus, out var linked);
        if (linked == 0)
            throw new InvalidOperationException(GL.GetProgramInfoLog(_program));

        GL.DetachShader(_program, vertex);
        GL.DetachShader(_program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        _uMvp = GL.GetUniformLocation(_program, "uMvp");
        _uColor = GL.GetUniformLocation(_program, "uColor");
        _uPointSize = GL.GetUniformLocation(_program, "uPointSize");
        _uRoundPoints = GL.GetUniformLocation(_program, "uRoundPoints");
    }

    private static int CompileShader(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var compiled);
        if (compiled == 0)
            throw new InvalidOperationException(GL.GetShaderInfoLog(shader));

        return shader;
    }

    private bool TryGetParticle(int index, out ParticlePreviewPoint particle)
    {
        particle = _data?.Particles.FirstOrDefault(p => p.Index == index)!;
        return particle != null;
    }

    private static float FixedGridStep(float radius)
    {
        if (radius < 0.2f)
            return 0.01f;
        if (radius < 0.7f)
            return 0.025f;
        if (radius < 2.0f)
            return 0.05f;
        if (radius < 5.0f)
            return 0.1f;
        return 0.25f;
    }

    private static bool IsFinite(Vector3 point) =>
        float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private bool HasTransformSelection() => GetTransformSelectionPositions().Count > 0;

    private List<Vector3> GetTransformSelectionPositions()
    {
        if (_data == null)
            return new List<Vector3>();

        return PickKind switch
        {
            PreviewPickKind.Bone => _data.Bones
                .Where(bone => bone.Index == SelectedBoneIndex)
                .Select(bone => ToGl(bone.Position))
                .ToList(),
            PreviewPickKind.Collider => _data.Colliders
                .Where(collider => collider.Index == SelectedColliderIndex)
                .Select(collider => (ToGl(collider.Start) + ToGl(collider.End)) * 0.5f)
                .ToList(),
            _ => _data.Particles
                .Where(particle => _selectedParticleIndices.Contains(particle.Index))
                .Select(particle => ToGl(particle.Position))
                .ToList()
        };
    }

    private void SetAxisConstraint(Keys key, Vector3 worldAxis)
    {
        if (_axisConstraintKey == key && _localAxisConstraint)
            return;

        var requestLocalAxis = _axisConstraintKey == key && PickKind != PreviewPickKind.Particle;
        _axisConstraintKey = key;
        _localAxisConstraint = requestLocalAxis;
        _axisConstraint = requestLocalAxis
            ? GetSelectedLocalWorldAxis(worldAxis)
            : ToGl(new SysVec3(worldAxis.X, worldAxis.Y, worldAxis.Z));
    }

    private Vector3 GetSelectedLocalWorldAxis(Vector3 fallback)
    {
        if (_data == null)
            return fallback;

        var boneIndex = PickKind switch
        {
            // Bone translations are stored in their parent's coordinate space.
            // A repeated axis key should therefore use the parent frame, not
            // the selected bone's rotated drawing axis.
            PreviewPickKind.Bone => _data.Bones.FirstOrDefault(bone => bone.Index == SelectedBoneIndex)?.ParentIndex ?? -1,
            PreviewPickKind.Collider => _data.Colliders.FirstOrDefault(collider => collider.Index == SelectedColliderIndex)?.BoneIndex ?? -1,
            _ => -1
        };
        var bone = _data.Bones.FirstOrDefault(candidate => candidate.Index == boneIndex);
        if (bone == null)
            return fallback;

        var axis = fallback.X > 0.5f ? ToGl(bone.AxisX)
            : fallback.Y > 0.5f ? ToGl(bone.AxisY)
            : ToGl(bone.AxisZ);
        return axis.LengthSquared < 0.000001f ? fallback : Vector3.Normalize(axis);
    }

    private static void AddLine(List<Vector3> vertices, Vector3 a, Vector3 b)
    {
        vertices.Add(a);
        vertices.Add(b);
    }

    private static Vector3 ToGl(SysVec3 vector)
    {
        // Havok/BotW and the OpenGL viewport use opposite X handedness. Keep
        // this conversion at the presentation boundary; stored HKCL values
        // stay untouched and editor transform events are converted back.
        return new Vector3(-vector.X, vector.Y, vector.Z);
    }

    private static float[] ToFloatArray(IReadOnlyList<Vector3> vertices)
    {
        var result = new float[vertices.Count * 3];
        for (var i = 0; i < vertices.Count; i++)
        {
            result[i * 3] = vertices[i].X;
            result[i * 3 + 1] = vertices[i].Y;
            result[i * 3 + 2] = vertices[i].Z;
        }

        return result;
    }

    private void DrawFallbackMessage(Graphics graphics, string title, string? details)
    {
        graphics.Clear(Color.White);
        using var brush = new SolidBrush(Color.FromArgb(70, 70, 70));
        graphics.DrawString(title, Font, brush, 12, 12);
        if (!string.IsNullOrWhiteSpace(details))
            graphics.DrawString(details, Font, brush, 12, 34);
    }
}
