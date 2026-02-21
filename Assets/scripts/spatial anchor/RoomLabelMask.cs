using System;

[Flags]
public enum RoomLabelMask
{
    NONE         = 0,
    FLOOR        = 1 << 0,
    CEILING      = 1 << 1,
    WALL_FACE    = 1 << 2,
    TABLE        = 1 << 3,
    COUCH        = 1 << 4,
    DOOR_FRAME   = 1 << 5,
    WINDOW_FRAME = 1 << 6,
    OTHER        = 1 << 7,
    STORAGE      = 1 << 8,
    BED          = 1 << 9,
    SCREEN       = 1 << 10,
    LAMP         = 1 << 11,
    PLANT        = 1 << 12,
    WALL_ART     = 1 << 13,

    EVERYTHING   = ~0
}
