# First visual motion-vector milestone

## Current milestone: motion-responsive output confirmed

Post-milestone direction revision: geometry and camera output now use current-to-previous; `CurrentToPrevious = 8` (combined convention 15). Debug direction colors reverse, speed remains unchanged, and wire-probe colors do not change. Earlier screenshots and numerical tests used the legacy forward direction. The revised sign is source/test-verified, pending a new in-game capture; it does not resolve outstanding accuracy issues.

September 6, 2026. Numerical and temporal correctness remain pending; this is not a production-quality or DLSS-readiness certification.

- With probe Off and a static camera, a moving grid produces nonzero visible motion while stationary voxel geometry remains gray.
- The latest capture uses final `Velocity`, scale 8 px, persistent gain 1, three-second half-life. Status shows `PERSISTENT / PAUSED` and 554/554 full shader pairs. Pause retention is now visually confirmed after removing the erroneous per-draw debug-history reset.
- The extended pink silhouette is a screen-space persistence trail, not stretched geometry. Retained peaks are not simultaneous per-frame measurements, and status counters remain live.
- Camera/third-person changes still produce saturated quadrant colors on the character. Their cause is unresolved; persistence can retain transition motion, so these images cannot validate skinning, camera reprojection, or magnitude.
- Earlier clean-environment MrtWrite captures established visible grid/voxel write coverage. Shader-pair counts establish participating shader coverage, not numerical accuracy.

Evidence attachments: 552e12f6 (paused status), 95c039ca (moving-grid trail), b3d8d6b9 (third-person quadrant). Source/build candidate remains in the working tree; no new milestone commit is claimed here.

Exit criteria: controlled signed pixel displacement; near-zero stationary baseline; same-frame raw/camera/composite comparison; finite-value and temporal-spike checks; previous-frame reprojection alignment excluding disocclusions; geometry-class coverage; resize, camera-cut, and consumer reset validation. See [DLSS motion-vector integration](DLSSMotionVectorIntegration.md).

## Historical first partial-write milestone

User-confirmed September 6, 2026, with the object-repair build.

- GBufferVelocityRaw / MrtWrite / Live: pink writes on much of grid geometry; some blocks and voxels remain cyan.
- Probe Off: user observes gradients responding both to moving grids with a static camera and to camera movement around static grids.
- This proves partial visible response, not complete coverage or numerical accuracy, sign, scale, or temporal stability.
- Status: verified VS 10/22, PS 22/22; repairs attempted 24, replaced 12, failed 12. During MrtWrite, PS full coverage is 1342/1342 but VS full and full pairs are zero.
- Common endpoint reports full VS 23/23 and PS 35/35. Consequently the failed vertex replacement step, not merely missing compilation flow, is the immediate investigation target.
- Probe Off disables native audit sampling; zero native-query counts in that mode are not evidence of failed binds.

Evidence supplied in conversation: dc578e91 (write image), c30254bc (write status), 937d20aa (motion image), 141b58c4 (motion status). Original images remain user attachments, not repository assets.

Milestone preserves the tested source, including accumulated renderer diagnostics and its required UI/support sources. Subsequent fixes must remain distinguishable from this baseline.

## Investigation after milestone c2274f7

The active Profile/SpaceEngineers_20260906_051211193.log identifies the vertex failures as SSGI Pipeline/vs.hlsl X3013: __vertex_shader has no matching three-parameter function. The logged unowned compile stacks are ShaderPackRegistry.CompileProbe validation work.

Inspection of the installed SSGI assembly confirms PrismShaderBundleManager replaces MyVertexShaders.Init and MyPixelShaders.Init compiler calls with its own FileShaderCompiler. PrismMaterialShaders similarly replaces the old pipeline compile route. This explains the previously missing compiler call sites; it is not evidence of CLR inlining. Its Pipeline/ps.hlsl emits Prism velocity at SV_Target3, and its GBufferVelocity class owns a separate RGBA16F texture. Anomaly also claims Target3, so adapting only the vertex call signature is insufficient and could break SSGI silently.

The project policy is not to add an SSGI-specific compatibility path. SSGI must migrate to Anomaly's consumer and shader-pack contracts, leaving compiler interception, GBuffer attachment allocation, and Target3 ownership to Anomaly. Testing continues with unrelated render plugins disabled. See [Compatibility.md](Compatibility.md).

## Clean-environment proof and motion-math candidate

September 6, 2026: user reports only Smooth Frames and DLSS remain enabled alongside development requirements. MrtWrite now covers visible grids and voxels in pink; the audit execution panel is green, and full shader pairs are 1143/1143 (805/805 in the tiled capture). This proves the instrumented write path, not numerical motion correctness. Probe Off still shows discontinuous saturated grid colors and gray voxels.

Two independently identified math defects are corrected in the next candidate:

- Object reconstruction used the cofactor matrix without transposing it and discarded the determinant sign. The inverse now uses the adjugate and signed determinant, including rotated and mirrored instances.
- Camera-only reconstruction mixed current-camera-relative positions with the previous camera's origin. Fullscreen depth reconstruction and geometry without explicit previous-world history now include current-minus-previous camera translation. Explicit history transforms already use the previous origin and are not offset twice.

The private vertex constant payload is now 240 bytes (physical allocation remains 256); probe mode remains at byte 212 and camera translation starts at byte 224. Pixel b7 remains a separate 16-byte payload. Deploy the DLL and shaders together. The published motion-vector convention is unchanged.

Validation: `scripts/test_velocity_math.py` exercises 500 seeded rotated/scaled/mirrored inverse cases, stationary reconstruction, camera translation, and explicit-history origin handling. `scripts/VelocityMathProbe.hlsl` smoke-compiles the actual simple-instancing header; its includes require the game's shader directory and generated Anomaly include directory. These checks do not replace GPU visual validation.

Pending in-game proof, with probe Off: stationary camera/geometry should yield near-zero motion; moving grids should remain coherent across rotated blocks; camera translation should produce smooth, depth-dependent motion on static geometry including voxels. Retain MrtWrite coverage as a separate write-path check. No claim of complete or consumer-ready motion accuracy is made yet.
