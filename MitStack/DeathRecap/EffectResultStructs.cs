using System.Runtime.InteropServices;

namespace MitStack.DeathRecap;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct AddStatusEffect
{
    public uint Unknown1;
    public uint RelatedActionSequence;
    public uint ActorId;
    public uint CurrentHp;
    public uint MaxHp;
    public ushort CurrentMp;
    public ushort Unknown3;
    public byte DamageShield;
    public byte EffectCount;
    public ushort Unknown6;
    public unsafe fixed byte Effects[64];
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct StatusEffectAddEntry
{
    public byte   EffectIndex;
    public byte   Unknown1;
    public ushort EffectId;
    public ushort StackCount;
    public ushort Unknown3;
    public float  Duration;
    public uint   SourceActorId;
}
