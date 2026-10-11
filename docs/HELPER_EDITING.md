# Helper and Support Bone Editing

BotW BPHYSSB and TotK BPHHB files describe driven bone motion. They are not complete model skeletons, and their base bones are reference frames rather than skeleton-parent edits.

## The Three Parts

- **Input bone**: the animated bone whose movement is measured relative to its configured rest/reference pose.
- **Response curve**: converts a measured movement into a response. Input is horizontal in the plot; output is vertical. Roll and Bend H/V are angular attributes in radians, not interchangeable XYZ Euler channels. Translation uses the file's model units.
- **Affected bone**: receives the response through a connection to one of its movement channels. Two responses can be added together.

## Create a Response

1. Select an affected helper in the Bones list.
2. Expand **Response curves**, then **New response curve**.
3. Choose an existing input bone and input movement. Set its input range and response scale. The new curve has three keys; Smooth response uses zero tangents for eased transitions. Otherwise it starts linear. Values clamp beyond the range.
4. Click **Create curve**. This creates an independent resource, without replacing any existing connection.
5. Expand **Connections**. Choose the affected movement, select the new curve as Driver response, and click **Apply connection**. Add response is optional and is summed with the first response.
6. Load a matching BFRES and animation to inspect the result. **Evaluate helpers** compares the graph response against animation alone without changing saved poses.

For example, input range -0.5 to 0.5 with response scale 0.5 creates outputs -0.25, 0 and 0.25. Connecting this to Roll produces a half-strength roll response within that range. This is a starting point, not a universal game tuning value.

BotW has separate output strength fields, including negative or zero values. TotK uses the curve itself for response strength. Editing an existing curve changes all connections using it; create a new curve when only one helper should change. Reassigning a connection leaves other helpers' shared outputs and poses intact.

## Disconnect or Delete

- **Not connected** removes that response when Apply connection is clicked. A helper with all six channels disconnected remains available in the first group so it can be reconnected.
- **Delete helper** removes the selected bone's affected helper behavior. Its model-bone name, any input role, other helpers, and shared curve/output resources remain. It does not remove a bone from the BFRES.
- **Delete group** removes a whole connected motion group. Use it only when the entire group is unwanted.

Deletion asks for confirmation. Creation, connection edits and deletion support undo/redo. Saving writes native helper data and synchronizes table counts. Individual deletion compacts TotK pose references; it does not purge unconnected curves/outputs that may be useful for authoring.

## Current Limits

- Curve creation uses existing input records. Creating driver records from a blank helper file is not yet implemented.
- Existing curve editing preserves native key counts and extrapolation settings. New curves use three keys with clamped ends.
- Existing BotW linear responses can be selected as connections, but their slope/intercept editor is not implemented. A new linear-shaped Hermite curve can be used instead when clamped endpoints are appropriate.
- TotK connection authoring requires one explicit pose binding per affected record. Separate position, rotation and aim solver records, or multiple distinct pose bindings, are rejected rather than guessed.
- Preview and native save/reload checks do not certify game-side motion. Keep backups and validate edited assets in-game.
