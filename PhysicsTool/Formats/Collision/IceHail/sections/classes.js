/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseSection = require("./base");
const BinaryFile = require("../binaryFile");

const CLASS_IDS = {
    // Basic classes, always used, have no extra data
    "hkClass"        : 0x33D42383,
    "hkClassMember"  : 0xB0EFA719,
    "hkClassEnum"    : 0x8A3609CF,
    "hkClassEnumItem": 0xCE6F8A6C,

    // root elements
    "hkRootLevelContainer": 0x2772C11E,
    "hkpPhysicsData"      : 0x47A8CA83,
    "hkpPhysicsSystem"    : 0xB3CC6E64,
    "hkpRigidBody"        : 0xCD2E69E5,

    // container classes
    "hkpListShape"          : 0xF2EC3ED5,
    "hkpStaticCompoundShape": 0xD029071E,
    "StaticCompoundInfo"    : 0x5115A202,

    // shapes
    "hkpBoxShape"             : 0xDCA2D1A7,
    "hkpSphereShape"          : 0x37AA32C9,
    "hkpBvCompressedMeshShape": 0xABAB6BB3,
    "hkpConvexVerticesShape"  : 0xC21C8B5A,

    // transformation classes
    "hkpConvexTranslateShape": 0xA3F8BF6A,
    "hkpConvexTransformShape": 0x3AEAAA63,
};

module.exports = class Classes extends BaseSection
{
    constructor()
    {
        super();
        this.file = new BinaryFile().setEndian("big");
        this.classes = new Map(
            ['hkClass', 'hkClassMember', 'hkClassEnum', 'hkClassEnumItem'].map(name => [name, 0])
        );
        this.classPos = {};
    }

    _scanClasses(chunk)
    {
        this.classes.set(chunk.constructor.name, 0);
        chunk.children.forEach(child => this._scanClasses(child));
    }

    _writeClass(name)
    {
        this.file.write('u32', CLASS_IDS[name]);
        this.file.write('u8', 0x09);

        this.classes.set(name, this.file.pos());
        this.file.writeString(name);
    }

    create(chunk)
    {
        this._scanClasses(chunk);
        this.classes.forEach((_, name) => this._writeClass(name));
        this.file.alignTo(16, 0xFF);
    }

    read(hkFile, sectionSize)
    {
        this.classes = new Map();
        const offsetStart = hkFile.pos();

        for(;;)
        {
            hkFile.skip(4);
            if(hkFile.pos() >= (offsetStart + sectionSize) || hkFile.read("u8") != 0x09) {
                break;
            }
            const nameOffset = hkFile.pos() - offsetStart;
            const name = hkFile.readString();
            this.classes.set(name, nameOffset);
            this.classPos[nameOffset] = name;
        }
        return this.classes;
    }

    getOffset(name)
    {
        return this.classes.get(name);
    }

    getName(offset)
    {
        return this.classPos[offset];
    }
};