namespace PCInfoScreenStudio.Models;

public enum MatrixBarStyle { Segments, Heart, Potion, Coin, Crystal, Skull, Lightning, Hourglass, Shield, Bomb, Arrows, Hearts }
public enum MatrixBarPattern { Solid, Segments, Ticks, Diagonal, Chevrons, Hearts }
public enum MatrixFlightDirection { RightToLeft, LeftToRight }
public enum MatrixAnimationStyle { Rocket, Crab, Cat, Starfield, Rainbow, Breathing, Wave, TwoTone, Fade, Gradient, Meteor }
public sealed record MatrixFlightOption(string Label, MatrixFlightDirection Value);
public sealed record MatrixBarPreset(MatrixBarStyle Value, string Label, string Colour, string? Badge, MatrixBarPattern Pattern);
public sealed record MatrixAnimationPreset(MatrixAnimationStyle Value, string Label, string Colour);

public static class MatrixBarPresets
{
    public static IReadOnlyList<MatrixBarPreset> All { get; } =
    [
        new(MatrixBarStyle.Segments,"Diamond segments","#E832C8","ruby",MatrixBarPattern.Segments),
        new(MatrixBarStyle.Heart,"Heart / solid","#FF3838","heart",MatrixBarPattern.Solid),
        new(MatrixBarStyle.Potion,"Potion / battery","#43D536","potion",MatrixBarPattern.Segments),
        new(MatrixBarStyle.Coin,"Coin / fine ticks","#FFD23B","coin",MatrixBarPattern.Ticks),
        new(MatrixBarStyle.Crystal,"Crystal / diagonal","#18BCE7","diamond",MatrixBarPattern.Diagonal),
        new(MatrixBarStyle.Skull,"Skull / chevrons","#A7BBCB","skull",MatrixBarPattern.Chevrons),
        new(MatrixBarStyle.Lightning,"Lightning / energy","#FFCD2C","lightning",MatrixBarPattern.Ticks),
        new(MatrixBarStyle.Hourglass,"Hourglass / diagonal","#F5BD39","hourglass",MatrixBarPattern.Diagonal),
        new(MatrixBarStyle.Shield,"Shield / battery","#54DA57","shield",MatrixBarPattern.Segments),
        new(MatrixBarStyle.Bomb,"Bomb / solid","#BE39D9","bomb",MatrixBarPattern.Solid),
        new(MatrixBarStyle.Arrows,"Arrow segments","#F43CCC",null,MatrixBarPattern.Chevrons),
        new(MatrixBarStyle.Hearts,"Heart pips","#FF3548",null,MatrixBarPattern.Hearts)
    ];
    public static MatrixBarPreset Get(MatrixBarStyle style) => All.FirstOrDefault(s => s.Value == style) ?? All[0];
}

public static class MatrixAnimationPresets
{
    public static IReadOnlyList<MatrixAnimationPreset> All { get; } =
    [
        new(MatrixAnimationStyle.Rocket, "Rocket", "#F030C8"),
        new(MatrixAnimationStyle.Crab, "Crab walk", "#FF4A45"),
        new(MatrixAnimationStyle.Cat, "Cat walk", "#B76CFF"),
        new(MatrixAnimationStyle.Starfield, "Starfield", "#FFD43B"),
        new(MatrixAnimationStyle.Rainbow, "Rainbow", "#21E6E8"),
        new(MatrixAnimationStyle.Breathing, "Breathing", "#21E6E8"),
        new(MatrixAnimationStyle.Wave, "Wave", "#21E6E8"),
        new(MatrixAnimationStyle.TwoTone, "Two Tone", "#F037CB"),
        new(MatrixAnimationStyle.Fade, "Fade", "#5D7CFF"),
        new(MatrixAnimationStyle.Gradient, "Gradient", "#21E6E8"),
        new(MatrixAnimationStyle.Meteor, "Meteor", "#FFD43B")
    ];
    public static MatrixAnimationPreset Get(MatrixAnimationStyle style) => All.FirstOrDefault(a => a.Value == style) ?? All[0];
}
