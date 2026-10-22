# Catalog targets: core and DX11 regression

The executable links production DX11 CatalogImages, BufferCatalog, ISharedBuffer
and the device-free CatalogContract. It loads the actual built Anomaly core by
reflection. The small engine adapter models Keen's pooled IRtvTexture ownership
and renderer update queue; its resources, clears, deferred command lists and
readbacks use real D3D11 WARP. This is an automated contract test, not a loaded-game
manager or visual-acceptance claim. Manager DisposeTex/pool/statistics and enqueue
semantics were checked against the installed game assemblies with ILSpy.

Build Anomaly without deployment, then this fixture for net48/net10. Run serially:

```powershell
dotnet build ClientPlugin/ClientPlugin.csproj -c Release -p:RunPostBuildEvent=Never
dotnet build Tests/CatalogTargetsDx11 -c Release
dotnet run --project Tests/CatalogTargetsDx11 -c Release -f net10.0 --no-build -- 'T:/Cursor Projects/Anomaly'
& 'Tests/CatalogTargetsDx11/bin/Release/net48/Anomaly.Dx11.exe' 'T:/Cursor Projects/Anomaly'
```

Expected143 checks each: all20 catalog constants and PublishedBuffer reflection
members/constructors; legacy0/1 and six DXGI formats with typed GPU pixels;
pending renderer/device activation and cancellation; wrong-owner/malformed/reserved
refusal; idempotence; resize and upload/target conversion rollback; a recorded GPU
read surviving replacement; resource and actual device replacement; worker-thread
registration/retirement;16 unregister cycles returning the pool to zero; current
structured stride/element count; volume-to-structured/2D transitions, inactive CPU payload release, malformed-input preservation, padded mip normalization and borrowed-resource ownership; and an actionable older-satellite refusal before
it receives a format it would misinterpret. Framework/device/game adapters outside
catalog allocation and publication are inert. No game installation/profile changes.
