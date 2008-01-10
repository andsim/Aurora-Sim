BEGIN TRANSACTION;

CREATE TABLE prims(
        UUID varchar(255) primary key,
        RegionUUID varchar(255),
        ParentID integer,
        CreationDate integer,
        Name varchar(255),
        SceneGroupID varchar(255),
        Text varchar(255),
        Description varchar(255),
        SitName varchar(255),
        TouchName varchar(255),
        CreatorID varchar(255),
        OwnerID varchar(255),
        GroupID varchar(255),
        LastOwnerID varchar(255),
        OwnerMask integer,
        NextOwnerMask integer,
        GroupMask integer,
        EveryoneMask integer,
        BaseMask integer,
        PositionX float,
        PositionY float,
        PositionZ float,
        GroupPositionX float,
        GroupPositionY float,
        GroupPositionZ float,
        VelocityX float,
        VelocityY float,
        VelocityZ float,
        AngularVelocityX float,
        AngularVelocityY float,
        AngularVelocityZ float,
        AccelerationX float,
        AccelerationY float,
        AccelerationZ float,
        RotationX float,
        RotationY float,
        RotationZ float,
        RotationW float, 
        ObjectFlags integer, 
        SitTargetOffsetX float NOT NULL default 0, 
        SitTargetOffsetY float NOT NULL default 0, 
        SitTargetOffsetZ float NOT NULL default 0, 
        SitTargetOrientW float NOT NULL default 0, 
        SitTargetOrientX float NOT NULL default 0, 
        SitTargetOrientY float NOT NULL default 0, 
        SitTargetOrientZ float NOT NULL default 0);

CREATE TABLE primshapes(UUID varchar(255) primary key,
        Shape integer,
        ScaleX float,
        ScaleY float,
        ScaleZ float,
        PCode integer,
        PathBegin integer,
        PathEnd integer,
        PathScaleX integer,
        PathScaleY integer,
        PathShearX integer,
        PathShearY integer,
        PathSkew integer,
        PathCurve integer,
        PathRadiusOffset integer,
        PathRevolutions integer,
        PathTaperX integer,
        PathTaperY integer,
        PathTwist integer,
        PathTwistBegin integer,
        ProfileBegin integer,
        ProfileEnd integer,
        ProfileCurve integer,
        ProfileHollow integer,
        Texture blob,
        ExtraParams blob);

CREATE TABLE terrain(
        RegionUUID varchar(255),
        Revision integer,
        Heightfield blob);

CREATE TABLE land(
        UUID varchar(255) primary key,
        RegionUUID varchar(255),
        LocalLandID string,
        Bitmap blob,
        Name varchar(255),
        Desc varchar(255),
        OwnerUUID varchar(255),
        IsGroupOwned string,
        Area integer,
        AuctionID integer,
        Category integer,
        ClaimDate integer,
        ClaimPrice integer,
        GroupUUID varchar(255),
        SalePrice integer,
        LandStatus integer,
        LandFlags string,
        LandingType string,
        MediaAutoScale string,
        MediaTextureUUID varchar(255),
        MediaURL varchar(255),
        MusicURL varchar(255),
        PassHours float,
        PassPrice string,
        SnapshotUUID varchar(255),
        UserLocationX float,
        UserLocationY float,
        UserLocationZ float,    
        UserLookAtX float,
        UserLookAtY float,
        UserLookAtZ float);

CREATE TABLE landaccesslist(
        LandUUID varchar(255),
        AccessUUID varchar(255),
        Flags string);

COMMIT;
