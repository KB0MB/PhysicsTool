/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const Vector3 = require("./vector3");

module.exports = class BoundingBox
{
    constructor(centerOrBox = new Vector3(), sizeHalf = new Vector3())
    {
        if (centerOrBox instanceof BoundingBox) {
            this.center = centerOrBox.center.copy();
            this.sizeHalf = centerOrBox.sizeHalf.copy();
        }else{
            this.center = centerOrBox;
            this.sizeHalf = sizeHalf;
        }
    }

    merge(box)
    {
        if(!box) {
            return new BoundingBox(this);
        }

        const boxMinMaxA = this.getMinMax();
        const boxMinMaxB = box.getMinMax();

        return BoundingBox.fromMinMax(
            Vector3.min(boxMinMaxA.min, boxMinMaxB.min),
            Vector3.max(boxMinMaxA.max, boxMinMaxB.max)
        );
    }

    scale(scalar)
    {
        return new BoundingBox(
            this.center,
            this.sizeHalf.scale(scalar)
        );
    }

    /**
     * calculates the bounding box from multiple vertices
     * @param {Vertex[]} vertices
     */
    static fromVertices(vertices)
    {
        let minVals = [ Infinity,  Infinity,  Infinity];
        let maxVals = [-Infinity, -Infinity, -Infinity];

        if(vertices.length == 0) {
            vertices = [[0.0, 0.0, 0.0]];
        }

        vertices.forEach(vertex =>
        {
            minVals = minVals.map((val, i) => Math.min(val, vertex[i]));
            maxVals = maxVals.map((val, i) => Math.max(val, vertex[i]));
        });

        return BoundingBox.fromMinMax(
            new Vector3(minVals),
            new Vector3(maxVals)
        );
    }

    static fromEntry(entry, box = new BoundingBox())
    {
        // shapes
        if(entry.vertices) {
            box = BoundingBox.fromVertices(entry.vertices);
        } else if(entry.sizeHalf) {
            box.sizeHalf = new Vector3(entry.sizeHalf);
        } else if(entry.radius) {
            box.sizeHalf = new Vector3(entry.radius);
        }

        // transforms
        if(entry.scale) {
            box.sizeHalf = box.sizeHalf.multiply(new Vector3(entry.scale));
        }
        if(entry.translate) {
            box.center = box.center.add(new Vector3(entry.translate));
        }

        return box;
    }

    static fromMinMax(vecMin, vecMax)
    {
        const halfSize = vecMax.subtract(vecMin).scale(0.5);
        return new BoundingBox(vecMin.add(halfSize), halfSize);
    }

    static fromTree(entry)
    {
        let mergedBox = undefined;

        const childArray = entry.parts || entry.children;
        if(childArray && childArray.length > 0)
        {
            mergedBox = childArray
                .map(BoundingBox.fromTree)
                .reduce((boxA, boxB) => boxA.merge(boxB));
        }

        return BoundingBox.fromEntry(entry, mergedBox);
    }

    getMinMax()
    {
        return {
            min: this.center.subtract(this.sizeHalf),
            max: this.center.add(this.sizeHalf),
        }
    }

    getAsObject()
    {
        return {
            center: this.center.vec,
            sizeHalf: this.sizeHalf.vec,
        };
    }
};
