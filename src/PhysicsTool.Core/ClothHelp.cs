using HKCLTool;
namespace PhysicsTool.Core;
public sealed record ClothHelpTopic(string Title, string Body);
public static class ClothHelp
{
    public static IReadOnlyList<ClothHelpTopic> Topics { get; } = Build();
    private static IReadOnlyList<ClothHelpTopic> Build()
    {
        var topics = new List<ClothHelpTopic>
        {
            new("Start here",
                "Double-click a cloth or helper group to open its editor.\n\n" +
                "HKCL: BotW cloth.\nBPHCL: TotK cloth.\nBPHYSSB: BotW support bones.\nBPHHB: TotK helper bones.\nHKRG: ragdoll bones, bodies and joints.\n\n" +
                "Open Actor lets you preview a model with its animations and physics. To edit the physics, open the actor's extracted files separately. Models and animations use BFRES/SBFRES.\n\n" +
                "Make sure to keep an original copy as a backup!"),
            new("Shortcuts",
                "Ctrl+O: open physics\nCtrl+S: save\nCtrl+Shift+S: save as\nCtrl+Z: undo\nCtrl+Shift+Z or Ctrl+Y: redo\nF1: documentation\n\n" +
                "Viewport: G move, R rotate, S scale. X/Y/Z selects an axis; Shift+X/Y/Z selects the other two axes. Enter confirms; Escape cancels.\n\n" +
                "A selects all; Alt+A clears selection. Ctrl+C copies; Ctrl+V pastes; Ctrl+Shift+V pastes mirrored. Delete removes the selection. F adds a constraint with two particles or a face with three.\n\n" +
                "Home frames all; numpad decimal frames selection. Numpad 1/3/7 selects front/right/top; Ctrl selects the opposite side. Space plays/pauses animation, or toggles simulation when no animation is selected.\n\n" +
                "In the donor list, M merges the selected entry. On macOS, Command replaces Ctrl for file and edit commands."),
            new("Particles and anchors",
                "Particles give cloth its shape. You can place them individually or move a selection together in the viewport.\n\n" +
                "Mark a particle Fixed to use it as an anchor. It won't move freely during simulation, but its bone binding can still carry it along with an animation. Attach to Bone snaps your selection to a bone, keeps its spacing, and makes the active particle a fixed anchor.\n\n" +
                "Enable Particles follow bones when you want bone edits to move weighted particles too. This uses their existing weights; it doesn't create new ones.\n\n" +
                "Mass affects how corrections are shared between particles. Inverse mass is normally 1 / mass; fixed particles use zero. Radius sets their contact size, and friction reduces sliding against colliders."),
            new("Constraints",
                "Constraints control how particles move relative to each other. Select two particles and press F, or choose Add constraint.\n\n" +
                "Distance keeps a pair near a rest length. It's a good starting point for the edges of your cloth.\n\n" +
                "Directed stretch limits how far B can move from A. Only B is corrected, so the order matters.\n\n" +
                "Bend/stretch keeps a pair within a minimum and maximum distance. Links between non-neighboring particles can help limit folding.\n\n" +
                "If a link seems to do nothing, check its stiffness and anchors: zero stiffness has no effect, and two fixed particles can't move each other.\n\n" +
                "The other constraint types have their own topics below. Some can be inspected or adjusted, but not created from scratch."),
            new("Build a blank cloth",
                "1. Choose New, give your cloth a name, and pick the target format.\n2. Add particles, place them, and mark your anchors Fixed.\n3. Connect pairs with distance constraints.\n4. Select three particles and press F to create a triangle and its edge links. Keep the points apart and out of a straight line.\n5. Try a short simulation and adjust any links that pull the cloth too tight.\n\n" +
                "Creating a cloth doesn't automatically bind it to a model. It also needs skin weights, output-bone mappings and actor registration before it can drive a model in-game."),
            new("Colliders",
                "Colliders help keep cloth from passing through the body. Spheres have a center and radius; capsules have two endpoints with rounded ends.\n\n" +
                "Box-select or Shift-click to move several colliders together. Their bone bindings make them follow animated bones and bone edits.\n\n" +
                "Check a particle's collider assignments if it passes straight through a visible collider. The collider's slot must be enabled for that particle.\n\n" +
                "In HKCL, changing the Bone field keeps the collider in place. Attach to Bone snaps it to the new bone and changes its binding. BPHCL collider creation and rebinding aren't supported yet.\n\n" +
                "If cloth gets trapped inside a collider, check its anchors and starting position before increasing stiffness."),
            new("Bones and model binding",
                "Bone positions are relative to their parent, so moving a parent carries its children with it. Rotation fields use XYZ radians; PhysicsTool handles the quaternion conversion when saving.\n\n" +
                "The loaded BFRES stays unchanged while you edit cloth bones. Simulation deforms the preview, but never writes to the model file. Bone names and skin weights must match; loading a model won't repair missing weights.\n\n" +
                "Match bones to model copies the BFRES rest poses to the cloth bones you check. Particles, rest constraints and attached colliders are adjusted together. Unchecked bones keep their local poses, so children still follow a matched parent. Undo restores the complete change. Supports HKCL and supported BPHCL layouts.\n\n" +
                "Where bone deletion is supported, surviving children keep their position. A bone can't be removed while bindings still depend on it. BPHCL bone creation and deletion aren't supported yet.\n\n" +
                "To reshape an existing cloth, turn off Particles follow bones, edit its simulation bones, and apply the changes. Then choose Fit cloth to bones. This updates particle positions, radii, rest constraints, animation references and output bindings so simulation starts from the edited shape. Masses and colliders stay unchanged. Undo restores the previous cloth shape without undoing your bone edits. HKCL and supported BPHCL layouts can use this action."),
            new("Resize bone branch",
                "Select a parent in the Bones tab and press S. Confirm the scale to open the temporary XYZ adjustment popup. You can also choose Resize bone branch. X/Y/Z restricts scaling to an axis; Shift+X/Y/Z scales the other two axes. Its position is the pivot. Descendant bone positions, bound cloth particles, particle radii and rest constraints resize together. Bone rotations, bone scale values and particle masses stay unchanged.\n\n" +
                "The Scale field uses a factor: 1.000 keeps the original size, 0.500 halves it, and 2.000 doubles it. You can keep adjusting the value in the temporary popup; each preview starts from the original shape. Apply keeps the result as one undo step. Cancel or closing the popup restores the original.\n\n" +
                "Resize attached colliders includes collision shapes bound to that branch. Other body colliders stay their original size, so check for new overlaps after shrinking a cloth.\n\n" +
                "Shared colliders are automatically made unique to the selected cloth before resizing, leaving other cloths unchanged. With Resize attached colliders unchecked, collision shapes keep their original world positions and sizes.\n\n" +
                "This rest-shape edit works with HKCL and supported BPHCL layouts, independent of Particles follow bones. It does not save changes to your BFRES. Native BPHCL resizing still requires a single rest pose; unsupported layouts are left unchanged."),
            new("Resize all cloths",
                "Choose All in the editor cloth dropdown, then Resize all cloths. All shows a read-only preview of every cloth; individual cloth edits, simulation and animation are disabled. Choosing a cloth again restores its normal controls without moving the camera.\n\n" +
                "The XYZ factors resize the whole file around the file origin (0, 0, 0), not each cloth's own root. Each preview starts from the original file. Reset restores the original size; Cancel or closing the popup restores the original file. Apply is one undo step.\n\n" +
                "Resize colliders scales shared collision shapes only once. When unchecked, colliders retain their original world positions and sizes; their attachment offsets are adjusted to preserve those positions. Bone scale values, rotations and particle masses stay unchanged. The loaded BFRES is not modified. Native BPHCL resizing requires a single rest pose and supported rest-shape data."),
            new("Support and helper bones",
                "Helper bones add movement based on other bones. An input bone supplies the motion, a response curve shapes it, and a connection applies it to an affected bone.\n\n" +
                "Select an affected bone and open Connections. Choose the movement you want to drive and its response. Add response lets you combine a second response; Not connected removes a connection. BotW has response strength fields, while TotK strength is adjusted in the curve.\n\n" +
                "New response curve creates an independent three-key curve. Choose its input bone, movement, range and scale, then assign it in Connections. Be careful when editing an existing shared curve: every helper using it will receive the change.\n\n" +
                "Base bone sets the helper graph's reference frame, not the model's bone hierarchy. Angular values use radians.\n\n" +
                "Delete helper removes one bone's helper behavior. Delete group removes the connected group. Neither deletes a bone from your BFRES.\n\n" +
                "New input records and separate TotK position/rotation/aim solvers can't be created here yet."),
            new("Ragdolls",
                "HKRG contains the full model skeleton, a smaller skeleton used by the ragdoll, rigid bodies and joints.\n\n" +
                "The Bones selector lets you switch between the two skeletons. Rigid bodies holds sphere/capsule size and placement; Constraints holds the existing joint limits, damping and friction. Angles use radians.\n\n" +
                "Align to bone matches a selected bone to one in the other skeleton. Pose edits update existing skeleton mappings. Moving ragdoll bones also moves their bodies and updates joint frames. Edit full model-bone poses in the inspector.\n\n" +
                "These tools work with the existing setup; they don't create new mappings, bodies or joints.\n\n" +
                "The stress preview helps you spot obvious problems, but it isn't the game's full ragdoll solver. Give your changes an in-game check before relying on them."),
            new("Merge and convert",
                "Open the file you want to add to, then load a donor and choose the cloth to merge. Clear donor closes the reference; Swap files lets you work in the other direction.\n\n" +
                "You can merge HKCL and BPHCL cloths across formats. PhysicsTool converts the selected donor cloth first, then adds it without replacing existing cloths or changing the donor. Unsupported data stops the merge without changing your file.\n\n" +
                "Conversion writes a separate file. BPHCL to HKCL offers per-cloth mass and constraint scales, since no single scale suits every asset.\n\n" +
                "Helper groups need matching formats to merge. Convert the donor first when moving between BotW and TotK."),
            new("Animation and simulation",
                "Use an animation to see how your cloth responds to movement. You can scrub the timeline to inspect a pose or enable Loop to watch it repeat. In Settings, 30 FPS plays at half speed and 60 FPS at normal speed.\n\n" +
                "Stop simulation before editing. Wind and Options affect the preview, not the saved game settings.\n\n" +
                "If cloth stretches or spikes, check fixed anchors, bone bindings, collider assignments and constraint lengths before changing stiffness.\n\n" +
                "The preview is useful for testing, but some movement can still differ in-game."),
            new("BPHYSICS settings",
                "BPHYSICS links a BotW actor to its cloth and support-bone files and supplies wind settings. It does not contain the cloth particles or constraints.\n\n" +
                "Open the file through Open Physics. Each cloth slot needs the exact HKCL cloth name and model base bone. Adding or renaming a slot does not change the HKCL itself. Shared wind affects every slot through its main and additive factors. File links use game-relative resource paths, not paths on your computer.\n\n" +
                "Wind drag controls how strongly cloth follows airflow. Lighter cloth usually needs less drag. Frequency controls gust timing, not strength. Hover over a field for more detail. Disabling cloth or support bones keeps their settings, and other physics sections are preserved.\n\n" +
                "The actor viewer's Export button extracts BFRES, cloth and support/helper-bone resources. Physics stays in its original format. Models are decompressed BFRES; external textures stay separate."),
            new("BPHCL editing limits",
                "You can edit particles, fixed/free state, numeric values, collision masks, basic link constraints and triangle faces in BPHCL.\n\n" +
                "Fit cloth to bones, Match bones to model and Resize bone branch also work with supported native BPHCL cloths. They rebuild the existing skin references and output bindings without converting the file to HKCL.\n\n" +
                "Some files contain packed contacts or operators that the editor can't safely rebuild. If an edit would break those references, PhysicsTool blocks it and leaves your file unchanged.\n\n" +
                "Bone creation/deletion, new colliders and automatic model output bindings aren't supported yet."),
            new("Collision files",
                "Collision opens supported HKRB/HKSC files, actor packs and TotK polytope BGYML. Import mesh accepts FBX, DAE, OBJ, glTF/GLB and BFRES as collision sources.\n\n" +
                "Choose meshes, material and output type. Check the scale and orientation in the preview, then generate. New source meshes default to Wood.\n\n" +
                "TotK polytope generation uses convex hulls. Split concave shapes into separate pieces to avoid filling their gaps.\n\n" +
                "Wii U/Switch HKRB and Wii U HKSC generation are available. Switch HKSC can be viewed and exported, but not generated. DAE/FBX exports contain collision geometry, not all physics metadata. Generated resources still need to be placed in the actor pack."),
            new("Shrine collision (BPHSC)",
                "BPHSC holds a shrine's compound collision, actor links and water. Open it through Collision > Static compound. Start from an existing shrine file and save a separate copy; the original is kept untouched.\n\n" +
                "Bodies lets you edit whole-body position and rotation. Rotations use radians. Actor links lets you change an existing actor hash; it must match the actor's placement ID in BANC. Water has position, rotation, stored dimensions, flow speed and fluid type. Waterfall dimensions and flow are preserved because their layout differs.\n\n" +
                "For moving platforms, GroupGUID connects the BANC GroundControl actor to a BPHSC body. GroupID must agree too. Apply group binding updates the existing body's table entries and actor links together. It does not move shapes between groups or create new groups. Keep the matching BANC values in sync yourself.\n\n" +
                "Check BANC compares GroundControl placement IDs and group bindings without changing the scene file. A successful check does not validate movement logic, AI, merged scenes or which collision shapes belong to the group.\n\n" +
                "This panel preserves embedded collision meshes unchanged. For coordinated actor moves and collision updates, use Shrine scene. Adding or removing collision shapes and creating a shrine from scratch are not supported yet. Save a copy and test it in-game before using it in your mod."),
            new("Shrine scenes",
                "Collision > Shrine scene lists the scenes in your TotK dump. Static and dynamic files share one entry, such as Dungeon000. Search for a scene and double-click it to open both parts, merged scenes and the matching BPHSC when available. Scenes returns to the list without closing your current scene.\n\n" +
                "The viewport shows placed actor models with textures. Click a model or select its actor in the list; double-click the list entry to frame its geometry. Search actors by name, label, hash or group, or show only GroundControl actors. Actor origins is optional. Hide selected and Hide filtered help you look inside rooms; Show all restores hidden models. These visibility changes are preview-only.\n\n" +
                "The inspector shows placement, group identifiers and dynamic parameters. Asset notices lists unavailable models and textures. The BPHSC button opens the paired collision data; its baked collision surfaces are not drawn yet. Some material effects still use approximations.\n\n" +
                "Click the viewport to navigate with W/A/S/D. Hold the right mouse button to look, use Q/E vertically, and hold Shift to move faster. Camera speed is beside Asset notices. With an actor selected, S starts scaling when you are not walking or holding the right mouse button.\n\n" +
                "Select an actor to edit its local position, rotation in radians, or XYZ scale. Apply each set of values with its button. In the viewport, G moves, R rotates and S scales; X/Y/Z restricts the transform to an axis, Enter confirms and Escape cancels. The model and supported collision update together. Undo and Redo restore both.\n\n" +
                "Ctrl-click selects several actors; Shift-click selects a range in the list. Move selected uses a world offset. Rotation offset and Scale factor work around the selection's center. Each operation is one undo step. A selected parent transforms its children too, without applying the operation twice to selected children.\n\n" +
                "Duplicate (Ctrl+D) creates independent copies at the same position and selects them. Press G to move them. Delete removes the selection and its baked collision after confirmation. Both actions support multiple actors and undo/redo. Copies receive new actor and placement IDs. Actors with scene links, AI references, physics constraints or GroundControl membership, and merged-scene parents, are protected; hover the disabled button for the reason.\n\n" +
                "Save shrine creates a separate mod folder containing both BANC companions, merged scenes and the rebuilt BPHSC. The dump stays untouched. Open source BPHSC inspects the original file, not your unsaved edits.\n\n" +
                "Select a GroundControl actor to edit an existing moving platform. Members lists the actors in its group. Movement exposes the native controller's travel, turn, wave or rail settings; hover a field for its meaning. Apply movement leaves the group's collision unchanged. Existing triggers and rails are preserved, not generated, and motion is not played in the editor yet.\n\n" +
                "Apply pivot updates the controller and its BPHSC collision frame. Leave Move members with pivot checked to reposition the whole platform, or uncheck it to change the pivot while keeping members and their collision in place. Pivot rotations use radians. Group identifiers updates GroupID and GroupGUID in both BANC members and BPHSC. Every change supports undo/redo and Save shrine. Groups with unresolved members, shared placement IDs, water or external meshes cannot be relocated; rails and physics constraints can also prevent whole-group moves. Movement settings may still be editable.\n\n" +
                "Existing collision instances support movement, rotation and positive scaling. Unequal XYZ scales can introduce shear when combined with rotated parents or collision shapes; those edits are rejected without changing the scene. Use uniform scaling in that case. New geometry, new GroundControl groups, adding/removing group members, water/external-mesh layouts and shared merged-scene children remain unsupported. Navigation meshes and AI paths are not rebuilt. Test your saved shrine in-game."),
            new("Cloth FBX export",
                "File > Export > Selected cloth as FBX exports the selected HKCL cloth in its edited rest pose, not the running simulation. The loaded BFRES is not included.\n\n" +
                "Particles become mesh vertices, with the cloth's existing triangle faces. Loose particles are kept. The skeleton retains its hierarchy and the cloth's existing particle skin weights.\n\n" +
                "Colliders become separate meshes attached to their bones. Planes use a finite preview patch; point colliders become empty objects.\n\n" +
                "This export is for geometry and skeleton work. FBX does not retain Havok constraints, masses, collision masks or simulation settings, and cannot be imported back as an HKCL."),
        };
        foreach (var kind in ClothConstraintCatalog.Kinds.Where(k => !k.CanCreate))
            topics.Add(new(kind.Name, kind.Description + "\n\nYou can inspect this type and adjust the available fields, but you can't create it from scratch yet."));
        return topics;
    }
}
