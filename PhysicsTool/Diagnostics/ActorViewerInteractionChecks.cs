using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HKCLTool;

internal static class ActorViewerInteractionChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboInfo
    {
        public int Size;
        public NativeRect Item, Button;
        public int ButtonState;
        public IntPtr Combo, Edit, List;
    }
    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr window, ref ComboInfo info);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr GetItemRect(IntPtr window, uint message, IntPtr wParam, ref NativeRect rectangle);
    public static void Run(BotwActorViewerForm form, string directory)
    {
        var scene = form.Scene!;
        var timer = Field<System.Windows.Forms.Timer>(form, "_timer");
        timer.Stop();
        try
        {
            CheckAnimations(form);
            var meshCount = scene.Meshes.Count;
            var boneCount = scene.RestModel.Bones.Count;
            Field<Button>(form, "_meshes").PerformClick(); Application.DoEvents();
            var meshes = Field<BotwActorMeshesForm>(form, "_meshesWindow");
            Require(!meshes.Controls.OfType<TabControl>().Any(), "Meshes window exposes material tabs.");
            var list = Field<ListView>(meshes, "_list");
            Require(list.Items.Count == meshCount, "Meshes window is missing actor meshes.");
            list.Items[0].Checked = false;
            Require(scene.CurrentModel.Meshes.Count == meshCount - 1 && form.Preview.VisualModel!.Meshes.Count == meshCount - 1, "Mesh checkbox did not update the viewport.");
            Invoke(form, "Render", false);
            Require(scene.CurrentModel.Meshes.Count == meshCount - 1, "Hidden mesh returned after a pose update.");
            var selected = Math.Min(1, meshCount - 1);
            list.Items[selected].Selected = true;
            Field<Button>(meshes, "_delete").PerformClick();
            Require(scene.Meshes[selected].Deleted && list.Items.Count == meshCount - 1, "Delete did not remove the selected mesh from the viewer list.");
            Field<Button>(form, "_reset").PerformClick();
            Require(!scene.Meshes[0].Visible && scene.Meshes[selected].Deleted, "Reset pose reset mesh visibility.");
            if (scene.HasCloth)
            {
                Field<CheckBox>(form, "_simulate").Checked = true;
                Invoke(form, "TickPlayback");
                Require(scene.CurrentModel.Meshes.Count < meshCount, "Simulation restored removed meshes.");
                Field<CheckBox>(form, "_simulate").Checked = false;
            }
            meshes.Refresh(); Application.DoEvents();
            using (var bitmap = new Bitmap(meshes.Width, meshes.Height))
            {
                meshes.DrawToBitmap(bitmap, new Rectangle(Point.Empty, meshes.Size));
                bitmap.Save(Path.Combine(directory, "meshes.png"));
            }
            foreach (var mesh in scene.Meshes) scene.SetMeshVisible(mesh.Index, false);
            Invoke(form, "Render", false);
            Require(scene.CurrentModel.Meshes.Count == 0 && scene.CurrentModel.Bones.Count == boneCount, "Hiding all meshes damaged the actor skeleton.");
            foreach (var mesh in scene.Meshes.Where(m => !m.Deleted)) scene.SetMeshVisible(mesh.Index, true);
            Invoke(form, "Render", false);
            Require(scene.CurrentModel.Meshes.Count == meshCount - 1, "Showing hidden meshes restored a deleted mesh.");
            Require(scene.RestModel.Meshes.Count == meshCount, "Mesh visibility modified the source model.");
            meshes.Close();
            Field<Button>(form, "_meshes").PerformClick(); Application.DoEvents();
            Require(Field<ListView>(Field<BotwActorMeshesForm>(form, "_meshesWindow"), "_list").Items.Count == meshCount - 1, "Reopening the mesh window restored a deleted mesh.");
            Field<BotwActorMeshesForm>(form, "_meshesWindow").Close();
            var reloaded = BotwActorScene.Load(scene.Resources.SourcePath, scene.Resources.Roots, actorEntry: scene.Resources.SourceEntry);
            form.ShowScene(reloaded, scene.Resources.SourcePath);
            Require(reloaded.CurrentModel.Meshes.Count == meshCount, "Reloading the actor did not recover the original model.");
            File.WriteAllText(Path.Combine(directory, "interactions.txt"), "PASS: session-only mesh hide/show/delete, pose and simulation persistence, non-simulated hover/cancel, and simulation commit-only animation selection.");
        }
        finally { Field<CheckBox>(form, "_simulate").Checked = false; timer.Start(); }
    }
    private static void CheckAnimations(BotwActorViewerForm form)
    {
        var scene = form.Scene!;
        if (scene.Animations.Count < 2) return;
        var combo = Field<ComboBox>(form, "_animations");
        var a = scene.Animations.First(x => x.FrameCount > 1);
        var b = scene.Animations.First(x => x != a);
        var position = Math.Min(3, a.FrameCount / 2.0);
        Field<Button>(form, "_reset").PerformClick();
        combo.DroppedDown = true; Hover(combo, b); Invoke(form, "TickPlayback");
        Require(scene.CurrentModel.AnimationSourcePath == b.Name, "Hover failed before any animation had been selected.");
        combo.DroppedDown = false; Application.DoEvents();
        Require(combo.SelectedIndex == -1 && scene.CurrentModel.AnimationSourcePath == "", "Cancel did not restore the unanimated pose.");
        combo.SelectedItem = a;
        Invoke(form, "Seek", position);
        combo.DroppedDown = true;
        Hover(combo, b);
        Invoke(form, "TickPlayback");
        Require(scene.CurrentModel.AnimationSourcePath == b.Name && scene.CurrentModel.AnimationFrame == 0,
            $"Paused hover did not preview the highlighted animation: expected={b.Name}, actual={scene.CurrentModel.AnimationSourcePath}, native={combo.SelectedItem}, browsing={Field<bool>(form, "_animationBrowsing")}, open={combo.DroppedDown}.");
        Require(Field<ToolboxActorAnimation>(form, "_selectedAnimation") == a && Field<double>(form, "_frame") == position, "Hover changed the committed animation or frame.");
        combo.DroppedDown = false; Application.DoEvents();
        Require(combo.SelectedItem == a && scene.CurrentModel.AnimationSourcePath == a.Name && scene.CurrentModel.AnimationFrame == position, "Cancel did not restore the selected animation pose.");

        Invoke(form, "SetPlaying", true);
        combo.DroppedDown = true; Hover(combo, b); Invoke(form, "TickPlayback");
        Require(Field<double>(form, "_frame") == position, "Hover preview advanced the original playback clock.");
        combo.DroppedDown = false; Application.DoEvents();
        Require(Field<bool>(form, "_playing"), "Cancel did not preserve playback state.");
        Invoke(form, "SetPlaying", false);

        combo.DroppedDown = true; Hover(combo, b, commit: true); Application.DoEvents();
        Require(Field<ToolboxActorAnimation>(form, "_selectedAnimation") == b && scene.CurrentModel.AnimationSourcePath == b.Name, "Non-simulated click did not commit the previewed clip.");
        combo.SelectedItem = a; Invoke(form, "Seek", position);

        if (scene.HasCloth)
        {
            Field<CheckBox>(form, "_simulate").Checked = true;
            combo.DroppedDown = true; Hover(combo, b);
            Invoke(form, "TickPlayback");
            Require(Field<ToolboxActorAnimation>(form, "_selectedAnimation") == a && scene.CurrentModel.AnimationSourcePath == a.Name && Field<double>(form, "_frame") == position,
                "Hover switched animation while simulation was active.");
            combo.DroppedDown = false; Application.DoEvents();
            Require(combo.SelectedItem == a && Field<double>(form, "_frame") == position, "Cancel reset the simulated animation.");
            combo.DroppedDown = true; Hover(combo, b, commit: true);
            combo.DroppedDown = false; Application.DoEvents();
            Require(Field<ToolboxActorAnimation>(form, "_selectedAnimation") == b && scene.CurrentModel.AnimationSourcePath == b.Name && Field<double>(form, "_frame") == 0,
                "Committed selection did not switch the simulated animation.");
            Field<CheckBox>(form, "_simulate").Checked = false;
        }
        Field<Button>(form, "_reset").PerformClick();
    }
    private static void Hover(ComboBox combo, ToolboxActorAnimation animation, bool commit = false)
    {
        // Send mouse messages only to this diagnostic's own native popup, so
        // tests exercise provisional highlights without moving the user's cursor.
        var info = new ComboInfo { Size = Marshal.SizeOf<ComboInfo>() };
        Require(GetComboBoxInfo(combo.Handle, ref info) && info.List != IntPtr.Zero, "The native animation dropdown is unavailable.");
        var index = combo.Items.IndexOf(animation);
        SendMessage(info.List, 0x0197, index, IntPtr.Zero); // LB_SETTOPINDEX
        var rectangle = new NativeRect();
        Require(GetItemRect(info.List, 0x0198, index, ref rectangle).ToInt64() != -1, "The highlighted row has no bounds.");
        var point = new IntPtr(((rectangle.Top + rectangle.Bottom) / 2 << 16) | (rectangle.Left + 12));
        var entryPoint = new IntPtr((rectangle.Top << 16) | (rectangle.Left + 4));
        SendMessage(info.List, 0x0200, IntPtr.Zero, entryPoint);
        SendMessage(info.List, 0x0200, IntPtr.Zero, point); // WM_MOUSEMOVE
        if (commit)
        {
            SendMessage(info.List, 0x0201, new IntPtr(1), point); // WM_LBUTTONDOWN
            SendMessage(info.List, 0x0202, IntPtr.Zero, point); // WM_LBUTTONUP
        }
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
