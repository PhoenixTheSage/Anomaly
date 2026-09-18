param([string]$EngineSource = 'C:\Users\Zeridian\.agents\skills\se-dev-game-code\Data\Decompiled\VRage.Render11')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$patch = Get-Content (Join-Path $root 'ClientPlugin/Patches/OwnedPassPatch.cs') -Raw
$late = Get-Content (Join-Path $root 'ClientPlugin/Patches/CameraVelocityPatch.cs') -Raw
$scheduler = Get-Content (Join-Path $EngineSource 'VRage/Render11/Render/MyRenderScheduler.cs') -Raw
$lighting = [regex]::Match($patch, '(?s)static class OwnedPassAfterLightingPatch.*?(?=\[HarmonyPatch\])').Value
$transparent = [regex]::Match($patch, '(?s)static class OwnedPassAfterTransparentPatch.*?(?=\[HarmonyPatch\])').Value
if ($lighting -notmatch 'typeof\(MyGBufferResolver\), "ConsumeWork"' -or $lighting -notmatch '\[HarmonyPostfix\]') { throw 'AfterLighting must run after lighting command-list consumption.' }
$depthAt = $lighting.IndexOf('OwnedBuffersPass.Execute();')
$shadowAt = $lighting.IndexOf('OwnedPassRegistry.Run(OwnedPassSlot.AfterLighting')
if ($depthAt -lt 0 -or $shadowAt -le $depthAt) { throw 'Current depth must precede AfterLighting consumers.' }
if ($late.Contains('OwnedBuffersPass.Execute();')) { throw 'Depth producer has returned to late Scheduler.Done.' }
if ($transparent -notmatch 'typeof\(MyTransparentRendering\), "ConsumeWork"' -or $transparent -notmatch '\[HarmonyPostfix\]') { throw 'AfterTransparent must run after transparency consumption.' }
if ($scheduler.IndexOf('MyGBufferResolver.ConsumeWork();') -ge $scheduler.IndexOf('MyTransparentRendering.ConsumeWork();')) { throw 'Engine consumption order changed; inspect renderer integration.' }
Write-Output 'PASS: source integration contract: lighting consumption -> current linear depth -> AfterLighting -> transparency consumption -> AfterTransparent. No late depth producer.'

$atlas = Get-Content (Join-Path $root 'ClientPlugin/ShaderFramework/PointShadowPass.cs') -Raw
if ($atlas.Contains('MyCommon.ProjectionConstants')) { throw 'Atlas must not read, map, restore or bind the engine-owned projection buffer.' }
if (!$atlas.Contains('DrawLocalCharacterMesh(rc, lights[i].WorldPos)') -or !$atlas.Contains('CubeFaceViewProj(Vector3.Zero, f, lights[i].Range)')) { throw 'Atlas geometry and projection must share the light origin, independent of viewer.' }
if (!$atlas.Contains('DrawLocalCharacterMesh(rc, env.CameraPosition, true)')) { throw 'Player mask must retain the scene camera origin.' }
if (!$atlas.Contains('CollectBoxes(lights[i], LightBoxes, MaxBoxesPerLight, skipActor, lights[i].WorldPos)')) { throw 'Optional atlas boxes must share the mesh origin.' }
Write-Output 'PASS: atlas owns its projection buffer and light-relative geometry; player mask retains camera-relative geometry.'
