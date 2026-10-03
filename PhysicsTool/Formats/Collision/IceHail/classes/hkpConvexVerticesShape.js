/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const {getCollisionFlag} = require("../flags");

const BoundingBox = require("../math/bounding-box");
const {writeVector4, writeInverseMatrix3x4, writeCounter, readCounter, readVector4} = require("../hkrbHelper");
const Vector3 = require("../math/vector3");
const {pointsToPlane, pointOnPlane} = require("../math/plane");

/**
 * Convex Vertices Shape
 * A convex mesh defined by plane equations and vertices
 * @extends {BaseClass}
 */
module.exports = class hkpConvexVerticesShape extends BaseClass
{
    _create()
    {
        const {vertices, indices} = this.collData;
        const matrixArray = this._groupVertices(vertices);

        this.file.skip(8);
        this.file.write('u32', 0x00040000);
        this.file.write('u32', getCollisionFlag(this.collData.flags));

        const planes = this._calculatePlanes(vertices, indices);

        writeVector4(this.file, [0.05]);

        const box = BoundingBox.fromVertices(vertices).scale(1.05).getAsObject();
        writeVector4(this.file, box.sizeHalf);
        writeVector4(this.file, box.center);

        this._setPointer(0);
        writeCounter(this.file, matrixArray.length);

        this.file.write('u32', vertices.length); // vertex count
        this.file.write('u32', 0); // padding

        this._setPointer(2);
        writeCounter(this.file, planes.length);
        this.file.skip(16);

        this._setPointer(1);
        matrixArray.forEach(matrix => writeInverseMatrix3x4(this.file, matrix)); // inverse-matrix of 4 vectors/vertices

        this._setPointer(3);
        planes.forEach(plane => writeVector4(this.file, plane)); // plane equations if the form of: ax + by + cz + d = 0
    }

    _groupVertices(vertices)
    {
        vertices = vertices || [];
        const matrixCount = Math.ceil(vertices.length / 4);
        const matrixArray = new Array(matrixCount);
        let i = -1;
        for(let m=0; m<matrixCount; ++m)
        {
            matrixArray[m] = [0,0,0,0].map(() =>
            {
                if(i < vertices.length-1)
                    ++i;

                if(vertices[i].length != 3)
                    throw "All vertices must contain 3 floats";

                return vertices[i];
            });
        }

        return matrixArray;
    }

    _calculatePlanes(vertices, indicesArray)
    {
        return indicesArray.map(indices =>
        {
            if(indices.length < 3)
                throw "A polygon must have at least 3 indices";

                const vectors = indices.slice(0,3).map(index => new Vector3(vertices[index]));
                return pointsToPlane(...vectors);
        });
    }

    read(chunk)
    {
        this.file.skip(64);
        const offsetVertices = chunk.dataLinks.get(this.file.pos());
        const countMatrix = readCounter(this.file);
        const countVertices = this.file.read("u32");

        this.file.skip(4);
        const offsetPlanes = chunk.dataLinks.get(this.file.pos());
        const countPlanes = readCounter(this.file);

        const data = {
            vertices: new Array(countVertices).fill(0).map(() => new Array(3)),
            indices: []
        };

        // read vertices from transposed matrices
        let vertexIndex = 0;
        this.file.pos(offsetVertices);
        for(let i=0; i<countMatrix; ++i)
        {
            for(let c=0; c<3; ++c)
            {
                for(let v=0; v<4; ++v)
                {
                    const val = this.file.read("float32");
                    if(vertexIndex + v < countVertices) {
                        data.vertices[vertexIndex + v][c] = val;
                    }
                }
            }
            vertexIndex += 4;
        }

        // read plane equations, and calculate touching points to get indices
        this.file.pos(offsetPlanes);
        for(let i=0; i<countPlanes; ++i)
        {
            const indexArray = [];
            const equation = readVector4(this.file, false);

            data.vertices.forEach((vertex, index) => {
                if(pointOnPlane(equation, new Vector3(vertex))) {
                    indexArray.push(index);
                }
            });

            data.indices.push(indexArray);
        }

        return data;
    }
};