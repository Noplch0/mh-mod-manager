using MhModManager.Core.Equipment;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

public sealed class EquipmentResolverTests
{
    [Fact]
    public void WorldArmorPathResolvesLeatherSet()
    {
        var hits = EquipmentResolver.Resolve(GameId.World, [
            @"nativePC\pl\f_equip\pl001_0000\mod\f_body001.mod3",
            @"nativePC\pl\f_equip\pl001_0000\mod\f_arm001.mod3"
        ]);

        var hit = Assert.Single(hits);
        Assert.Equal("女装备", hit.Type);
        Assert.Equal("【皮制】服装", hit.Name);
        Assert.Equal(2, hit.FileCount);
        Assert.False(hit.IsPfb);
    }

    [Fact]
    public void WorldWeaponPathResolvesNamedBow()
    {
        var hits = EquipmentResolver.Resolve(GameId.World, [
            "nativepc/wp/bow/bow001/mod/bow001.mod3"
        ]);

        var hit = Assert.Single(hits);
        Assert.Equal("弓", hit.Type);
        Assert.Equal("钢冰马弓/钢龙强弓/钢龙射手弓", hit.Name);
    }

    [Fact]
    public void WorldLayeredWeaponUsesOpFilesToPickSpecificName()
    {
        var hits = EquipmentResolver.Resolve(GameId.World, [
            "nativepc/wp/bow/bs_bow001/mod/bs_bow001.mod3",
            "nativepc/wp/bow/bs_bow001/parts/op/op_bow001.mod3"
        ]);

        Assert.Contains(hits, hit => hit.Name.StartsWith("铁弓", StringComparison.Ordinal));
        Assert.Contains(hits, hit => hit.Name == "灭尽龙大弓");
    }

    [Fact]
    public void TextureFilesAreIgnored()
    {
        var hits = EquipmentResolver.Resolve(GameId.World, [
            "nativepc/pl/f_equip/pl001_0000/mod/f_body001.tex.10"
        ]);

        Assert.Empty(hits);
    }

    [Fact]
    public void RiseArmorAndWeaponPathsResolveChineseNames()
    {
        var armor = EquipmentResolver.Resolve(GameId.Rise, [
            "natives/stm/player/mod/f/pl001/f_body001.mesh"
        ]);
        var weapon = EquipmentResolver.Resolve(GameId.Rise, [
            "natives/weapon/G_Swd/G_Swd001/mod.mesh"
        ]);

        Assert.Equal("皮制/皮制S/皮制X", Assert.Single(armor).Name);
        Assert.Equal("女装备", armor[0].Type);
        Assert.Equal("钢龙寒冰大剑", Assert.Single(weapon).Name);
        Assert.Equal("大剑", weapon[0].Type);
    }

    [Fact]
    public void WildsArmorWeaponAndPrefabPathsResolve()
    {
        var female = EquipmentResolver.Resolve(GameId.Wilds, [
            "natives/stm/art/model/character/ch03/001/001/body.mesh"
        ]);
        var cat = EquipmentResolver.Resolve(GameId.Wilds, [
            "natives/art/model/character/ch05/001/0000/body.mesh"
        ]);
        var bow = EquipmentResolver.Resolve(GameId.Wilds, [
            "natives/art/model/item/it11/00/0000/bow.mesh"
        ]);
        var pfb = EquipmentResolver.Resolve(GameId.Wilds, [
            "natives/gamedesign/equip/_prefab/armor/female/001/001/armor.pfb.17"
        ]);

        Assert.Equal("希望α(女款)", Assert.Single(female).Name);
        Assert.Equal("女装备", female[0].Type);
        Assert.Equal("希望猫(身)", Assert.Single(cat).Name);
        Assert.Equal("随从猫", cat[0].Type);
        Assert.Equal("希望之弓/期冀之弓", Assert.Single(bow).Name);
        Assert.True(Assert.Single(pfb).IsPfb);
        Assert.Equal("希望α(女款)", pfb[0].Name);
        Assert.Contains("[pfb]", pfb[0].Display);
    }

    [Fact]
    public void WorldArmorFileNameWinsWhenFolderWasAlreadyRenamed()
    {
        var hits = EquipmentResolver.Resolve(GameId.World, [
            @"nativePC\pl\f_equip\pl001_0000\body\mod\f_body108_0000.mod3"
        ]);

        var hit = Assert.Single(hits);
        Assert.Equal("女装备", hit.Type);
        Assert.Equal(1_080_000, hit.Id);
    }

    [Fact]
    public void UnknownWorldArmorStillReturnsFallbackLabel()
    {
        var hits = EquipmentResolver.Resolve(GameId.World, [
            "nativepc/pl/m_equip/pl999_0000/mod/body.mod3"
        ]);

        var hit = Assert.Single(hits);
        Assert.Equal("男装备", hit.Type);
        Assert.Equal("男装备@9990000", hit.Name);
    }
}
