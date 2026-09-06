# First visual motion-vector milestone

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
