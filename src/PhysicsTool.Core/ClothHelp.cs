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
                "Match bones to model copies the BFRES rest poses to the cloth bones you check. Particles, rest constraints and attached colliders are adjusted together. Unchecked bones keep their local poses, so children still follow a matched parent. Undo restores the complete change. HKCL only.\n\n" +
                "Where bone deletion is supported, surviving children keep their position. A bone can't be removed while bindings still depend on it. BPHCL bone creation and deletion aren't supported yet.\n\n" +
                "To resize an existing HKCL cloth, turn off Particles follow bones, edit its simulation bones, and apply the changes. Then choose Fit cloth to bones. This reshapes the particles and updates their radii, rest constraints and output bindings so simulation starts from the edited shape. Masses and colliders stay unchanged. Undo restores the previous cloth shape without undoing your bone edits. This action is not available for BPHCL yet."),
            new("Resize bone branch",
                "Select a parent in the Bones tab and press S. Confirm the scale to open the temporary XYZ adjustment popup. You can also choose Resize bone branch. X/Y/Z restricts scaling to an axis; Shift+X/Y/Z scales the other two axes. Its position is the pivot. Descendant bone positions, bound cloth particles, particle radii and rest constraints resize together. Bone rotations, bone scale values and particle masses stay unchanged.\n\n" +
                "The Scale field uses a factor: 1.000 keeps the original size, 0.500 halves it, and 2.000 doubles it. You can keep adjusting the value in the temporary popup; each preview starts from the original shape. Apply keeps the result as one undo step. Cancel or closing the popup restores the original.\n\n" +
                "Resize attached colliders includes collision shapes bound to that branch. Other body colliders stay their original size, so check for new overlaps after shrinking a cloth.\n\n" +
                "This is an HKCL rest-shape edit, independent of Particles follow bones. It does not save changes to your BFRES. BPHCL is not supported by this action yet."),
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
            new("BPHCL editing limits",
                "You can edit particles, fixed/free state, numeric values, collision masks, basic link constraints and triangle faces in BPHCL.\n\n" +
                "Some files contain packed contacts or operators that the editor can't safely rebuild. If an edit would break those references, PhysicsTool blocks it and leaves your file unchanged.\n\n" +
                "Bone creation/deletion, new colliders and automatic model output bindings aren't supported yet."),
            new("Collision files",
                "Collision opens supported HKRB/HKSC files, actor packs and TotK polytope BGYML. Import mesh accepts FBX, DAE, OBJ, glTF/GLB and BFRES as collision sources.\n\n" +
                "Choose meshes, material and output type. Check the scale and orientation in the preview, then generate. New source meshes default to Wood.\n\n" +
                "TotK polytope generation uses convex hulls. Split concave shapes into separate pieces to avoid filling their gaps.\n\n" +
                "Wii U/Switch HKRB and Wii U HKSC generation are available. Switch HKSC can be viewed and exported, but not generated. DAE/FBX exports contain collision geometry, not all physics metadata. Generated resources still need to be placed in the actor pack."),
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
