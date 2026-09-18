#ifndef ANOMALY_VOLUME_INTERIORS_HLSLI
#define ANOMALY_VOLUME_INTERIORS_HLSLI
// Grid records: camera-relative->grid-local matrix (4 rows), then
// (cell size, first cell, cell count, valid). Cells are signed xyz sorted
// lexicographically. Occupancy is independent of room oxygen quantity.
StructuredBuffer<float4> VolumeInteriorGrids : register(t23);
StructuredBuffer<int4> VolumeInteriorCells : register(t24);

bool VolumeCellBefore(int3 a,int3 b)
{
    return a.x < b.x || (a.x == b.x && (a.y < b.y || (a.y == b.y && a.z < b.z)));
}

bool AnomalyInsideSealedRoom(float3 cameraRelativePosition,uint gridCount)
{
    for(uint grid=0;grid<gridCount;grid++)
    {
        uint start=grid*7;
        float4 info=VolumeInteriorGrids[start+4];
        if(info.w < .5) continue;
        row_major float4x4 transform=float4x4(VolumeInteriorGrids[start],VolumeInteriorGrids[start+1],
            VolumeInteriorGrids[start+2],VolumeInteriorGrids[start+3]);
        float3 local=mul(float4(cameraRelativePosition,1),transform).xyz / max(info.x,.01);
        int3 cell=(int3)floor(local+.5);
        if(any(cell < (int3)VolumeInteriorGrids[start+5].xyz) ||
           any(cell > (int3)VolumeInteriorGrids[start+6].xyz)) continue;
        uint low=(uint)info.y, high=low+(uint)info.z;
        while(low<high)
        {
            uint middle=low+(high-low)/2;
            int3 candidate=VolumeInteriorCells[middle].xyz;
            if(all(candidate==cell)) return true;
            if(VolumeCellBefore(candidate,cell)) low=middle+1;
            else high=middle;
        }
    }
    return false;
}
#endif
