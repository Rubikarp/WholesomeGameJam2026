var LightSideGpuUploadPlugin = {

    $LightSideGpuUploadV6: {
        magic: 0x3647534c,
        abiMajor: 6,
        abiMinor: 0,
        contractFingerprint: 0x4336534c,
        slotBytes: 0x100000,
        maxSubmissionStagingBytes: 0x10000000,
        maxResidentSlots: 3,
        maxRegionsPerSubmission: 1024,
        batchAssumeNonOverlapping: 1,
        batchOrderedOverlaps: 2,
        batchObserveSharedGlErrors: 4,
        regionPublicationMarker: 1,
        supportRegional: 1,
        supportWholeMipOnly: 2,
        supportEdgeRemainder: 4,
        supportExplicitPitches: 8,
        supportPowerOfTwo: 16,
        supportTopMipBlockMultiple: 32,
        supportPaddedRowRepack: 64,
        epochLow: 1,
        epochHigh: 0,
        epochExhausted: false,
        nextToken: 1,
        tokenHigh: 1,
        slots: [],
        nextSlotId: 1,
        context: null,
        canvas: null,
        contextLossPending: false,
        contextLostHandler: null,
        contextRestoredHandler: null,
        targets: {},
        sequences: {},
        sequenceHistory: [],
        sequenceHistoryCapacity: 64,
        maxSequences: 64,
        sequenceCount: 0,
        nextSequenceLow: 1,
        nextSequenceHigh: 0,
        formatCache: [],
        norm16Extension: undefined,
        compressedFormatSet: undefined,
        astcProfiles: undefined,
        limits: null,
        scratchBuffer: null,
        scratchBytes: 0,
        scratchViews: [],
        resolveError: 0,
        stats: {
            accepted: { low: 0, high: 0 },
            rejected: { low: 0, high: 0 },
            encoded: { low: 0, high: 0 },
            stale: { low: 0, high: 0 },
            backpressure: { low: 0, high: 0 },
            encodedPayloadBytes: { low: 0, high: 0 }
        },

        error: {
            none: 0,
            notInitialized: 1,
            abiMismatch: 2,
            unsupportedFeature: 4,
            invalidArgument: 5,
            invalidLayout: 6,
            unsupportedTexture: 7,
            unsupportedDimension: 8,
            unsupportedFormat: 9,
            unsupportedSampleCount: 10,
            targetNotFound: 11,
            targetStale: 12,
            targetBusy: 13,
            targetClosing: 14,
            sourceOutOfRange: 15,
            backpressure: 16,
            backendFailed: 18,
            contextLost: 19,
            stateRestoreFailed: 20,
            outOfMemory: 21,
            sequenceNotFound: 25,
            sequenceClosing: 26,
            sequenceOrder: 27,
            internal: 24,
            slotNotFound: 28,
            slotBusy: 29
        },

        stageLayout: 1,
        stageSlot: 2,
        stageTarget: 3,
        stageRegion: 4,
        stageMarker: 5,
        stageOverlap: 6,
        stageBackend: 7,

        findSlot: function(id) {
            for (var index = 0; index < this.slots.length; index++)
                if (this.slots[index].id === id) return this.slots[index];
            return null;
        },

        freeSlot: function(slot) {
            if (slot.state === 0) return;
            slot.state = 0;
            if (slot.transient) {
                var index = this.slots.indexOf(slot);
                if (index >= 0) this.slots.splice(index, 1);
                _free(slot.base);
                return;
            }
            slot.generation = slot.generation === 0xffffffff ? 1 : (slot.generation + 1) >>> 0;
        },

        trimFreeSlots: function() {
            for (var index = this.slots.length - 1; index >= 0; index--) {
                var slot = this.slots[index];
                if (slot.state !== 0) continue;
                _free(slot.base);
                this.slots.splice(index, 1);
            }
        },

        reset: function() {
            this.trimFreeSlots();
            this.targets = {};
            this.sequences = {};
            this.sequenceHistory = [];
            this.sequenceCount = 0;
            this.formatCache = [];
            this.norm16Extension = undefined;
            this.compressedFormatSet = undefined;
            this.astcProfiles = undefined;
            this.limits = null;
            this.scratchBuffer = null;
            this.scratchBytes = 0;
            this.scratchViews = [];
            this.context = null;
            if (this.epochExhausted)
                return false;
            if (this.epochLow === 0xffffffff && this.epochHigh === 0xffffffff) {
                this.epochExhausted = true;
                return false;
            }
            if (this.epochLow === 0xffffffff) {
                this.epochHigh = (this.epochHigh + 1) >>> 0;
                this.epochLow = 1;
            }
            else {
                this.epochLow = (this.epochLow + 1) >>> 0;
            }
            return true;
        },

        markContextLost: function() {
            if (this.contextLossPending) return;
            this.reset();
            this.contextLossPending = true;
        },

        attachContextEvents: function(context) {
            var canvas = context.canvas;
            if (!canvas || typeof canvas.addEventListener !== 'function' || this.canvas === canvas)
                return;
            if (this.canvas && typeof this.canvas.removeEventListener === 'function') {
                this.canvas.removeEventListener('webglcontextlost', this.contextLostHandler, false);
                this.canvas.removeEventListener('webglcontextrestored', this.contextRestoredHandler, false);
            }
            if (!this.contextLostHandler) {
                this.contextLostHandler = function(event) {
                    if (event && typeof event.preventDefault === 'function') event.preventDefault();
                    LightSideGpuUploadV6.markContextLost();
                };
                this.contextRestoredHandler = function() {
                    LightSideGpuUploadV6.contextLossPending = false;
                    LightSideGpuUploadV6.context = null;
                };
            }
            canvas.addEventListener('webglcontextlost', this.contextLostHandler, false);
            canvas.addEventListener('webglcontextrestored', this.contextRestoredHandler, false);
            this.canvas = canvas;
        },

        ensureContext: function() {
            try {
                if (this.epochExhausted)
                    return this.error.internal;
                if (typeof GLctx === 'undefined' || !GLctx || typeof GL === 'undefined')
                    return this.error.notInitialized;
                if (typeof GLctx.texSubImage3D !== 'function')
                    return this.error.unsupportedFeature;
                this.attachContextEvents(GLctx);
                var contextState = this.safeContextLost(GLctx);
                if (contextState < 0) return this.error.internal;
                if (contextState > 0) {
                    this.markContextLost();
                    return this.error.contextLost;
                }
                if (this.contextLossPending) {
                    this.contextLossPending = false;
                    this.context = null;
                }
                if (this.context && this.context !== GLctx && !this.reset())
                    return this.error.internal;
                this.context = GLctx;
                return this.error.none;
            } catch (exception) {
                return this.error.internal;
            }
        },

        safeContextLost: function(gl) {
            try {
                return gl && typeof gl.isContextLost === 'function' && gl.isContextLost() ? 1 : 0;
            } catch (exception) {
                return -1;
            }
        },

        exceptionError: function() {
            var contextState = this.safeContextLost(this.context);
            if (contextState > 0) {
                try { this.markContextLost(); } catch (exception) {}
                return this.error.contextLost;
            }
            return this.error.internal;
        },

        clearBytes: function(pointer, bytes) {
            for (var offset = 0; offset < bytes; offset += 4)
                HEAPU32[(pointer + offset) >> 2] = 0;
        },

        fits: function(pointer, bytes, alignment) {
            pointer = pointer >>> 0;
            bytes = bytes >>> 0;
            return pointer !== 0 && (pointer & (alignment - 1)) === 0 &&
                pointer <= HEAPU8.length && bytes <= HEAPU8.length - pointer;
        },

        sameEpoch: function(pointer) {
            return HEAPU32[(pointer + 8) >> 2] === this.epochLow &&
                HEAPU32[(pointer + 12) >> 2] === this.epochHigh;
        },

        targetForHandle: function(pointer) {
            var low = HEAPU32[pointer >> 2];
            var high = HEAPU32[(pointer + 4) >> 2];
            var target = low === 0 ? null : this.targets[low];
            return target && target.tokenHigh === high ? target : null;
        },

        writeHandle: function(pointer, target) {
            HEAPU32[pointer >> 2] = target.tokenLow;
            HEAPU32[(pointer + 4) >> 2] = target.tokenHigh;
            HEAPU32[(pointer + 8) >> 2] = target.generation;
            HEAPU32[(pointer + 12) >> 2] = 0;
        },

        addCounter: function(name, value) {
            var counter = this.stats[name];
            var lowAddition = value >>> 0;
            var highAddition = Math.floor(value / 4294967296) >>> 0;
            var low = counter.low + lowAddition;
            counter.low = low >>> 0;
            counter.high = (counter.high + highAddition + (low > 0xffffffff ? 1 : 0)) >>> 0;
        },

        writeCounter: function(pointer, counter) {
            HEAPU32[pointer >> 2] = counter.low;
            HEAPU32[(pointer + 4) >> 2] = counter.high;
        },

        writeValue64: function(pointer, value) {
            HEAPU32[pointer >> 2] = value >>> 0;
            HEAPU32[(pointer + 4) >> 2] = Math.floor(value / 4294967296) >>> 0;
        },

        sequenceKey: function(low, high) {
            return (high >>> 0) + ':' + (low >>> 0);
        },

        findSequence: function(low, high) {
            var key = this.sequenceKey(low, high);
            var live = this.sequences[key];
            if (live) return live;
            for (var index = 0; index < this.sequenceHistory.length; index++) {
                var retained = this.sequenceHistory[index];
                if (retained.serialLow === (low >>> 0) && retained.serialHigh === (high >>> 0))
                    return retained;
            }
            return null;
        },

        allocateSequenceSerial: function() {
            if (this.nextSequenceLow === 0 && this.nextSequenceHigh === 0) return null;
            var serial = { low: this.nextSequenceLow, high: this.nextSequenceHigh };
            if (this.nextSequenceLow === 0xffffffff) {
                this.nextSequenceLow = 0;
                this.nextSequenceHigh = (this.nextSequenceHigh + 1) >>> 0;
            } else {
                this.nextSequenceLow = (this.nextSequenceLow + 1) >>> 0;
            }
            return serial;
        },

        writeSequenceStatus: function(pointer, sequence) {
            this.clearBytes(pointer, 80);
            HEAPU32[pointer >> 2] = 80;
            HEAPU32[(pointer + 4) >> 2] = sequence.state;
            HEAPU32[(pointer + 8) >> 2] = sequence.gpuState;
            HEAPU32[(pointer + 12) >> 2] = sequence.contentState;
            HEAPU32[(pointer + 16) >> 2] = sequence.publicationState;
            HEAP32[(pointer + 20) >> 2] = sequence.resultCode;
            HEAP32[(pointer + 24) >> 2] = sequence.failedBatch;
            HEAP32[(pointer + 28) >> 2] = sequence.failedRegion;
            HEAPU32[(pointer + 32) >> 2] = sequence.backendDetail;
            HEAPU32[(pointer + 36) >> 2] = sequence.flags;
            HEAPU32[(pointer + 40) >> 2] = sequence.expectedBatchCount;
            HEAPU32[(pointer + 44) >> 2] = sequence.admittedBatchCount;
            HEAPU32[(pointer + 48) >> 2] = sequence.sourceConsumedBatchCount;
            HEAPU32[(pointer + 52) >> 2] = sequence.encodedBatchCount;
            HEAPU32[(pointer + 56) >> 2] = sequence.gpuTerminalBatchCount;
            HEAPU32[(pointer + 60) >> 2] = sequence.retiredBatchCount;
            HEAPU32[(pointer + 64) >> 2] = sequence.serialLow;
            HEAPU32[(pointer + 68) >> 2] = sequence.serialHigh;
            HEAPU32[(pointer + 72) >> 2] = sequence.epochLow;
            HEAPU32[(pointer + 76) >> 2] = sequence.epochHigh;
        },

        sequenceHasTarget: function(sequence, target) {
            for (var index = 0; index < sequence.targetEntries.length; index++)
                if (sequence.targetEntries[index] === target) return true;
            return false;
        },

        retireTargetIfPossible: function(target) {
            if (target.state !== 2 || target.leases !== 0) return;
            target.state = 3;
            target.texture = null;
        },

        releaseSequenceTargetLeases: function(sequence) {
            if (sequence.targetLeasesReleased) return;
            sequence.targetLeasesReleased = true;
            for (var index = 0; index < sequence.targetEntries.length; index++) {
                var target = sequence.targetEntries[index];
                if (target.leases !== 0) target.leases--;
                this.retireTargetIfPossible(target);
            }
            sequence.targetEntries = [];
            sequence.flags |= 2;
        },

        finishSequenceBatch: function(sequence, publicationTerminal, error,
            failedRegion, detail, writes) {
            if (!sequence) return;
            sequence.sourceConsumedBatchCount++;
            sequence.gpuTerminalBatchCount++;
            sequence.retiredBatchCount++;
            if (error === this.error.none) {
                sequence.encodedBatchCount++;
                sequence.contentState = 1;
                if (publicationTerminal) sequence.publicationState = 3;
                if (sequence.expectedBatchCount !== 0 &&
                    sequence.encodedBatchCount === sequence.expectedBatchCount) {
                    sequence.state = 5;
                    this.releaseSequenceTargetLeases(sequence);
                }
                return;
            }
            sequence.expectedBatchCount = sequence.admittedBatchCount;
            sequence.gpuState = 3;
            sequence.contentState = sequence.encodedBatchCount !== 0 || writes !== 0 ? 2 : 0;
            sequence.resultCode = error;
            sequence.failedBatch = sequence.admittedBatchCount - 1;
            sequence.failedRegion = failedRegion;
            sequence.backendDetail = detail >>> 0;
            if ((sequence.configurationFlags & 1) !== 0)
                sequence.publicationState = (error === this.error.contextLost ||
                    (publicationTerminal && writes !== 0)) ? 4 : 2;
            sequence.state = 3;
            this.releaseSequenceTargetLeases(sequence);
        },

        abortSequence: function(sequence) {
            if (sequence.state !== 1 && sequence.state !== 2)
                return this.error.sequenceClosing;
            sequence.expectedBatchCount = sequence.admittedBatchCount;
            sequence.contentState = sequence.encodedBatchCount === 0 ? 0 : 2;
            if ((sequence.configurationFlags & 1) !== 0) sequence.publicationState = 2;
            sequence.state = 4;
            this.releaseSequenceTargetLeases(sequence);
            return this.error.none;
        },

        drainErrors: function(gl) {
            var observation = { first: gl.NO_ERROR, complete: false, threw: false,
                contextLost: false };
            try {
                for (var index = 0; index < 16; index++) {
                    var error = gl.getError();
                    if (typeof error !== 'number') {
                        observation.threw = true;
                        break;
                    }
                    if (error === gl.NO_ERROR) {
                        observation.complete = true;
                        break;
                    }
                    if (observation.first === gl.NO_ERROR) observation.first = error;
                }
            } catch (exception) {
                observation.threw = true;
            }
            if (observation.first !== gl.NO_ERROR || !observation.complete || observation.threw) {
                try {
                    observation.contextLost = gl.isContextLost();
                } catch (exception) {}
            }
            return observation;
        },

        errorObservationFailed: function(observation, gl) {
            return observation.first !== gl.NO_ERROR || !observation.complete || observation.threw;
        },

        logicalFormatLayout: function(format, aspect) {
            var layout = {
                blockWidth: 1,
                blockHeight: 1,
                blockDepth: 1,
                bytesPerBlock: 0,
                minimumBlocksX: 1,
                minimumBlocksY: 1,
                minimumBlocksZ: 1,
                supportedAspects: 1,
                compressed: false,
                depthStencil: false,
                pvrtc: false
            };
            if (format >= 1 && format <= 52) {
                if (aspect !== 0) return null;
                var componentBytes = format <= 20 ? 1 : format <= 36 ? 2 :
                    format <= 44 ? 4 : format <= 48 ? 2 : 4;
                layout.bytesPerBlock = (((format - 1) & 3) + 1) * componentBytes;
                return layout;
            }
            if (format >= 53 && format <= 62) {
                if (aspect !== 0) return null;
                layout.bytesPerBlock = (format & 1) === 0 ? 4 : 3;
                return layout;
            }
            if (format >= 63 && format <= 69) {
                if (aspect !== 0) return null;
                layout.bytesPerBlock = 2;
                return layout;
            }
            if (format >= 70 && format <= 77) {
                if (aspect !== 0) return null;
                layout.bytesPerBlock = 4;
                return layout;
            }
            if (format >= 78 && format <= 83) {
                layout.depthStencil = true;
                layout.supportedAspects = format === 80 || format === 82 ? 6 :
                    format === 83 ? 4 : 2;
                if ((layout.supportedAspects & (1 << aspect)) === 0) return null;
                layout.bytesPerBlock = aspect === 2 ? 1 : format === 78 ? 2 : 4;
                return layout;
            }
            if (format === 142) {
                layout.depthStencil = true;
                layout.supportedAspects = 6;
                if ((layout.supportedAspects & (1 << aspect)) === 0) return null;
                layout.bytesPerBlock = aspect === 1 ? 2 : 1;
                return layout;
            }
            if (aspect !== 0) return null;
            if (format >= 84 && format <= 97) {
                layout.compressed = true;
                layout.blockWidth = layout.blockHeight = 4;
                layout.bytesPerBlock = format === 84 || format === 85 ||
                    format === 90 || format === 91 ? 8 : 16;
                return layout;
            }
            if (format >= 98 && format <= 105) {
                layout.compressed = true;
                layout.pvrtc = true;
                layout.blockWidth = format === 98 || format === 99 ||
                    format === 102 || format === 103 ? 8 : 4;
                layout.blockHeight = 4;
                layout.bytesPerBlock = 8;
                layout.minimumBlocksX = layout.minimumBlocksY = 2;
                return layout;
            }
            if (format >= 106 && format <= 116) {
                layout.compressed = true;
                layout.blockWidth = layout.blockHeight = 4;
                layout.bytesPerBlock = format === 111 || format === 112 ||
                    format === 115 || format === 116 ? 16 : 8;
                return layout;
            }
            if (format >= 117 && format <= 134) {
                var variant = format <= 128 ? Math.floor((format - 117) / 2) :
                    format - 129;
                var blockSizes = [4, 5, 6, 8, 10, 12];
                if (variant < 0 || variant >= blockSizes.length) return null;
                layout.compressed = true;
                layout.blockWidth = layout.blockHeight = blockSizes[variant];
                layout.bytesPerBlock = 16;
                return layout;
            }
            if (format === 135) {
                layout.blockWidth = 2;
                layout.bytesPerBlock = 4;
                return layout;
            }
            if (format === 136 || format === 137 || format === 140 || format === 141) {
                layout.bytesPerBlock = 4;
                return layout;
            }
            if (format === 138 || format === 139) {
                layout.bytesPerBlock = 8;
                return layout;
            }
            return null;
        },

        ensureCompressedFormats: function() {
            if (this.compressedFormatSet !== undefined) return;
            var gl = this.context;
            var extensions = [
                'WEBGL_compressed_texture_s3tc',
                'WEBKIT_WEBGL_compressed_texture_s3tc',
                'WEBGL_compressed_texture_s3tc_srgb',
                'EXT_texture_compression_rgtc',
                'EXT_texture_compression_bptc',
                'WEBGL_compressed_texture_pvrtc',
                'WEBKIT_WEBGL_compressed_texture_pvrtc',
                'WEBGL_compressed_texture_etc',
                'WEBGL_compressed_texture_etc1'
            ];
            for (var index = 0; index < extensions.length; index++)
                gl.getExtension(extensions[index]);
            var astc = gl.getExtension('WEBGL_compressed_texture_astc');
            this.astcProfiles = [];
            if (astc && typeof astc.getSupportedProfiles === 'function')
                this.astcProfiles = astc.getSupportedProfiles() || [];
            var supported = gl.getParameter(gl.COMPRESSED_TEXTURE_FORMATS) || [];
            var set = {};
            for (var formatIndex = 0; formatIndex < supported.length; formatIndex++)
                set[supported[formatIndex] >>> 0] = true;
            this.compressedFormatSet = set;
        },

        compressedTokenSupported: function(token) {
            this.ensureCompressedFormats();
            return this.compressedFormatSet[token >>> 0] === true;
        },

        describeCompressedFormat: function(code) {
            var token = 0;
            var blockWidth = 4;
            var blockHeight = 4;
            var bytesPerBlock = 16;
            var minimumBlocksX = 1;
            var minimumBlocksY = 1;
            var pvrtc = false;
            var hdr = false;
            var arrayCompatible = false;
            var topMipBlockMultiple = code >= 84 && code <= 97;
            if (code === 84) { token = 0x8c4d; bytesPerBlock = 8; }
            else if (code === 85) { token = 0x83f1; bytesPerBlock = 8; }
            else if (code === 86) token = 0x8c4e;
            else if (code === 87) token = 0x83f2;
            else if (code === 88) token = 0x8c4f;
            else if (code === 89) token = 0x83f3;
            else if (code === 90) { token = 0x8dbc; bytesPerBlock = 8; }
            else if (code === 91) { token = 0x8dbb; bytesPerBlock = 8; }
            else if (code === 92) token = 0x8dbe;
            else if (code === 93) token = 0x8dbd;
            else if (code === 94) token = 0x8e8e;
            else if (code === 95) token = 0x8e8f;
            else if (code === 96) token = 0x8e8d;
            else if (code === 97) token = 0x8e8c;
            else if (code >= 98 && code <= 105) {
                if ((code & 1) === 0) return null;
                pvrtc = true;
                minimumBlocksX = 2;
                minimumBlocksY = 2;
                blockWidth = code === 99 || code === 103 ? 8 : 4;
                token = code === 99 ? 0x8c01 : code === 101 ? 0x8c00 :
                    code === 103 ? 0x8c03 : 0x8c02;
                bytesPerBlock = 8;
            } else if (code === 106) {
                token = this.compressedTokenSupported(0x9274) ? 0x9274 : 0x8d64;
                arrayCompatible = token === 0x9274;
                bytesPerBlock = 8;
            } else if (code === 107) { token = 0x9275; bytesPerBlock = 8; arrayCompatible = true; }
            else if (code === 108) { token = 0x9274; bytesPerBlock = 8; arrayCompatible = true; }
            else if (code === 109) { token = 0x9277; bytesPerBlock = 8; arrayCompatible = true; }
            else if (code === 110) { token = 0x9276; bytesPerBlock = 8; arrayCompatible = true; }
            else if (code === 111) { token = 0x9279; arrayCompatible = true; }
            else if (code === 112) { token = 0x9278; arrayCompatible = true; }
            else if (code === 113) { token = 0x9271; bytesPerBlock = 8; arrayCompatible = true; }
            else if (code === 114) { token = 0x9270; bytesPerBlock = 8; arrayCompatible = true; }
            else if (code === 115) { token = 0x9273; arrayCompatible = true; }
            else if (code === 116) { token = 0x9272; arrayCompatible = true; }
            else if (code >= 117 && code <= 134) {
                var variant = code <= 128 ? Math.floor((code - 117) / 2) : code - 129;
                var astcOffsets = [0, 2, 4, 7, 11, 13];
                var astcBlocks = [4, 5, 6, 8, 10, 12];
                if (variant < 0 || variant >= astcOffsets.length) return null;
                blockWidth = blockHeight = astcBlocks[variant];
                hdr = code >= 129;
                token = (code <= 128 && (code & 1) === 1 ? 0x93d0 : 0x93b0) +
                    astcOffsets[variant];
            } else {
                return null;
            }
            if (!this.compressedTokenSupported(token)) return null;
            if (hdr && this.astcProfiles.indexOf('hdr') < 0) return null;
            return {
                compressed: true,
                internalFormat: token,
                heapKind: 0,
                elementBytes: 1,
                blockWidth: blockWidth,
                blockHeight: blockHeight,
                blockDepth: 1,
                bytesPerBlock: bytesPerBlock,
                minimumBlocksX: minimumBlocksX,
                minimumBlocksY: minimumBlocksY,
                minimumBlocksZ: 1,
                wholeMipOnly: pvrtc,
                powerOfTwo: pvrtc,
                topMipBlockMultiple: pvrtc || topMipBlockMultiple,
                arrayCompatible: arrayCompatible
            };
        },

        describeFormat: function(code) {
            var cached = this.formatCache[code];
            if (cached !== undefined) return cached;

            var gl = this.context;
            if (code >= 84 && code <= 134) {
                cached = this.describeCompressedFormat(code);
                this.formatCache[code] = cached;
                return cached;
            }
            var channels = 0;
            var bits = 0;
            var kind = 0;
            if (code === 3 || code === 4) {
                channels = code;
                bits = 8;
                kind = 0;
            } else if (code >= 5 && code <= 20) {
                channels = ((code - 5) & 3) + 1;
                bits = 8;
                kind = (code - 5) >> 2;
            } else if (code >= 21 && code <= 36) {
                channels = ((code - 21) & 3) + 1;
                bits = 16;
                kind = (code - 21) >> 2;
            } else if (code >= 37 && code <= 44) {
                channels = ((code - 37) & 3) + 1;
                bits = 32;
                kind = 2 + ((code - 37) >> 2);
            } else if (code >= 45 && code <= 48) {
                channels = code - 44;
                bits = 16;
                kind = 4;
            } else if (code >= 49 && code <= 52) {
                channels = code - 48;
                bits = 32;
                kind = 4;
            } else if (code === 63 || code === 65 || code === 67) {
                cached = {
                    format: code === 65 ? gl.RGB : gl.RGBA,
                    type: code === 63 ? gl.UNSIGNED_SHORT_4_4_4_4 :
                        code === 65 ? gl.UNSIGNED_SHORT_5_6_5 : gl.UNSIGNED_SHORT_5_5_5_1,
                    heapKind: 2,
                    bytesPerPixel: 2,
                    elementBytes: 2,
                    requiresNorm16: false,
                    compressed: false,
                    blockWidth: 1,
                    blockHeight: 1,
                    blockDepth: 1,
                    bytesPerBlock: 2,
                    minimumBlocksX: 1,
                    minimumBlocksY: 1,
                    minimumBlocksZ: 1
                };
                this.formatCache[code] = cached;
                return cached;
            } else if (code === 70 || code === 71 || code === 72 || code === 73) {
                cached = {
                    format: code === 73 ? gl.RGBA_INTEGER :
                        code >= 72 ? gl.RGBA : gl.RGB,
                    type: code === 70 ? gl.UNSIGNED_INT_5_9_9_9_REV :
                        code === 71 ? gl.UNSIGNED_INT_10F_11F_11F_REV :
                        gl.UNSIGNED_INT_2_10_10_10_REV,
                    heapKind: 4,
                    bytesPerPixel: 4,
                    elementBytes: 4,
                    requiresNorm16: false,
                    compressed: false,
                    blockWidth: 1,
                    blockHeight: 1,
                    blockDepth: 1,
                    bytesPerBlock: 4,
                    minimumBlocksX: 1,
                    minimumBlocksY: 1,
                    minimumBlocksZ: 1
                };
                this.formatCache[code] = cached;
                return cached;
            } else {
                this.formatCache[code] = null;
                return null;
            }

            var normalized = kind === 0 || kind === 1;
            var signed = kind === 1 || kind === 3;
            var floating = (bits === 16 && kind === 4) || (bits === 32 && kind === 4);
            var integer = !normalized && !floating;
            var format = channels === 1 ? (integer ? gl.RED_INTEGER : gl.RED) :
                channels === 2 ? (integer ? gl.RG_INTEGER : gl.RG) :
                channels === 3 ? (integer ? gl.RGB_INTEGER : gl.RGB) :
                (integer ? gl.RGBA_INTEGER : gl.RGBA);
            var type;
            var heapKind;
            if (floating && bits === 16) { type = gl.HALF_FLOAT; heapKind = 2; }
            else if (floating) { type = gl.FLOAT; heapKind = 6; }
            else if (bits === 8 && signed) { type = gl.BYTE; heapKind = 1; }
            else if (bits === 8) { type = gl.UNSIGNED_BYTE; heapKind = 0; }
            else if (bits === 16 && signed) { type = gl.SHORT; heapKind = 3; }
            else if (bits === 16) { type = gl.UNSIGNED_SHORT; heapKind = 2; }
            else if (signed) { type = gl.INT; heapKind = 5; }
            else { type = gl.UNSIGNED_INT; heapKind = 4; }

            cached = {
                format: format,
                type: type,
                heapKind: heapKind,
                bytesPerPixel: channels * (bits >> 3),
                elementBytes: bits >> 3,
                requiresNorm16: bits === 16 && normalized,
                compressed: false,
                blockWidth: 1,
                blockHeight: 1,
                blockDepth: 1,
                bytesPerBlock: channels * (bits >> 3),
                minimumBlocksX: 1,
                minimumBlocksY: 1,
                minimumBlocksZ: 1
            };
            this.formatCache[code] = cached;
            return cached;
        },

        sourceHeap: function(info) {
            if (info.heapKind === 0) return HEAPU8;
            if (info.heapKind === 1) return HEAP8;
            if (info.heapKind === 2) return HEAPU16;
            if (info.heapKind === 3) return HEAP16;
            if (info.heapKind === 4) return HEAPU32;
            if (info.heapKind === 5) return HEAP32;
            return HEAPF32;
        },

        scratchHeap: function(info) {
            var cached = this.scratchViews[info.heapKind];
            if (cached) return cached;
            if (info.heapKind === 0) cached = new Uint8Array(this.scratchBuffer);
            else if (info.heapKind === 1) cached = new Int8Array(this.scratchBuffer);
            else if (info.heapKind === 2) cached = new Uint16Array(this.scratchBuffer);
            else if (info.heapKind === 3) cached = new Int16Array(this.scratchBuffer);
            else if (info.heapKind === 4) cached = new Uint32Array(this.scratchBuffer);
            else if (info.heapKind === 5) cached = new Int32Array(this.scratchBuffer);
            else cached = new Float32Array(this.scratchBuffer);
            this.scratchViews[info.heapKind] = cached;
            return cached;
        },

        ensureScratch: function(bytes) {
            bytes = bytes >>> 0;
            if (bytes <= this.scratchBytes) return true;
            var capacity = this.scratchBytes > 0 ? this.scratchBytes : 256;
            while (capacity < bytes) {
                if (capacity >= 0x40000000) {
                    capacity = bytes;
                    break;
                }
                capacity *= 2;
            }
            this.scratchBuffer = new ArrayBuffer(capacity);
            this.scratchBytes = capacity;
            this.scratchViews = [];
            return true;
        },

        regionLayout: function(info, width, height, depth, layerCount) {
            var blocksX = Math.max(info.minimumBlocksX,
                Math.ceil(width / info.blockWidth));
            var blocksY = Math.max(info.minimumBlocksY,
                Math.ceil(height / info.blockHeight));
            var blocksZ = Math.max(info.minimumBlocksZ,
                Math.ceil(depth / info.blockDepth));
            var rowBytes = blocksX * info.bytesPerBlock;
            var imageBytes = rowBytes * blocksY;
            var layerBytes = imageBytes * blocksZ;
            var totalBytes = layerBytes * layerCount;
            if (rowBytes > 0xffffffff || imageBytes > 0xffffffff ||
                layerBytes > 0xffffffff || totalBytes > 0xffffffff)
                return null;
            return {
                blocksX: blocksX,
                blockRows: blocksY,
                blocksZ: blocksZ,
                rowBytes: rowBytes,
                imageBytes: imageBytes,
                layerBytes: layerBytes,
                totalBytes: totalBytes
            };
        },

        requiresRepack: function(info, source, rowPitch, imagePitch, layerPitch,
            layout, depth, layerCount) {
            if (info.compressed)
                return rowPitch !== layout.rowBytes || imagePitch !== layout.imageBytes ||
                    layerPitch !== layout.layerBytes;
            return source % info.elementBytes !== 0 ||
                rowPitch % info.elementBytes !== 0 ||
                (depth > 1 && imagePitch % info.elementBytes !== 0) ||
                (layerCount > 1 && layerPitch % info.elementBytes !== 0);
        },

        repackRegion: function(source, rowPitch, imagePitch, layerPitch,
            layout, layerCount) {
            var scratch = new Uint8Array(this.scratchBuffer);
            var destination = 0;
            for (var layer = 0; layer < layerCount; layer++) {
                var layerSource = source + layer * layerPitch;
                for (var image = 0; image < layout.blocksZ; image++) {
                    var imageSource = layerSource + image * imagePitch;
                    for (var row = 0; row < layout.blockRows; row++) {
                        var rowSource = imageSource + row * rowPitch;
                        scratch.set(HEAPU8.subarray(rowSource, rowSource + layout.rowBytes),
                            destination);
                        destination += layout.rowBytes;
                    }
                }
            }
        },

        bindingBitForDimension: function(dimension) {
            if (dimension === 1) return 1;
            if (dimension === 2) return 2;
            if (dimension === 3) return 4;
            if (dimension === 4) return 8;
            return 0;
        },

        supportsFormatDimension: function(format, dimension) {
            var info = this.describeFormat(format);
            if (info === null || this.bindingBitForDimension(dimension) === 0) return false;
            if (info.compressed && dimension === 3) return false;
            if (info.compressed && dimension === 2 && !info.arrayCompatible) return false;
            if (info.wholeMipOnly && dimension !== 1 && dimension !== 4) return false;
            if (!info.requiresNorm16) return true;
            if (this.norm16Extension === undefined)
                this.norm16Extension = this.context.getExtension('EXT_texture_norm16') || null;
            return this.norm16Extension !== null;
        },

        supportsShape: function(dimension, width, height, depth, layers) {
            if (width === 0) return true;
            if (!this.limits) {
                var gl = this.context;
                this.limits = {
                    texture: gl.getParameter(gl.MAX_TEXTURE_SIZE),
                    texture3D: gl.getParameter(gl.MAX_3D_TEXTURE_SIZE),
                    layers: gl.getParameter(gl.MAX_ARRAY_TEXTURE_LAYERS),
                    cube: gl.getParameter(gl.MAX_CUBE_MAP_TEXTURE_SIZE)
                };
            }
            if (dimension === 1)
                return width <= this.limits.texture && height <= this.limits.texture;
            if (dimension === 2)
                return width <= this.limits.texture && height <= this.limits.texture &&
                    layers <= this.limits.layers;
            if (dimension === 3)
                return width <= this.limits.texture3D && height <= this.limits.texture3D &&
                    depth <= this.limits.texture3D;
            if (dimension === 4)
                return width <= this.limits.cube && height <= this.limits.cube;
            return false;
        },

        regionsOverlap: function(first, second, dimension) {
            var firstX = HEAP32[(first + 12) >> 2];
            var firstY = HEAP32[(first + 16) >> 2];
            var secondX = HEAP32[(second + 12) >> 2];
            var secondY = HEAP32[(second + 16) >> 2];
            if (firstX + HEAPU32[(first + 24) >> 2] <= secondX ||
                secondX + HEAPU32[(second + 24) >> 2] <= firstX ||
                firstY + HEAPU32[(first + 28) >> 2] <= secondY ||
                secondY + HEAPU32[(second + 28) >> 2] <= firstY)
                return false;
            if (dimension === 3) {
                var firstZ = HEAP32[(first + 20) >> 2];
                var secondZ = HEAP32[(second + 20) >> 2];
                return firstZ + HEAPU32[(first + 32) >> 2] > secondZ &&
                    secondZ + HEAPU32[(second + 32) >> 2] > firstZ;
            }
            if (dimension === 2 || dimension === 4) {
                var firstLayer = HEAPU32[(first + 36) >> 2];
                var secondLayer = HEAPU32[(second + 36) >> 2];
                return firstLayer + HEAPU32[(first + 40) >> 2] > secondLayer &&
                    secondLayer + HEAPU32[(second + 40) >> 2] > firstLayer;
            }
            return true;
        },

        validateRegistration: function(pointer) {
            if (!this.fits(pointer, 64, 8) || HEAPU32[pointer >> 2] !== 64 ||
                HEAPU32[(pointer + 56) >> 2] !== 0 || HEAPU32[(pointer + 60) >> 2] !== 0)
                return this.error.invalidArgument;

            var flags = HEAPU32[(pointer + 4) >> 2];
            var textureId = HEAPU32[(pointer + 16) >> 2];
            var textureHigh = HEAPU32[(pointer + 20) >> 2];
            if ((flags & ~31) !== 0 || (textureId === 0 && textureHigh === 0))
                return this.error.invalidArgument;
            if (!this.sameEpoch(pointer)) return this.error.targetStale;

            var width = HEAPU32[(pointer + 24) >> 2];
            var height = HEAPU32[(pointer + 28) >> 2];
            var depth = HEAPU32[(pointer + 32) >> 2];
            var layers = HEAPU32[(pointer + 36) >> 2];
            var mipCount = HEAPU32[(pointer + 40) >> 2];
            var format = HEAPU32[(pointer + 44) >> 2];
            var dimension = HEAPU32[(pointer + 48) >> 2];
            var samples = HEAPU32[(pointer + 52) >> 2];
            if (samples !== 1) return this.error.unsupportedSampleCount;
            if (width === 0 || height === 0 || depth === 0 || layers === 0 || mipCount === 0 ||
                width > 0x7fffffff || height > 0x7fffffff || depth > 0x7fffffff ||
                layers > 0x7fffffff || mipCount > 0x7fffffff)
                return this.error.unsupportedDimension;
            if ((dimension === 1 && (depth !== 1 || layers !== 1)) ||
                (dimension === 2 && depth !== 1) ||
                (dimension === 3 && layers !== 1) ||
                (dimension === 4 && (width !== height || depth !== 1 || layers !== 6)))
                return this.error.unsupportedDimension;
            if (this.bindingBitForDimension(dimension) === 0) return this.error.unsupportedDimension;

            var mipDimension = Math.max(width, height);
            if (dimension === 3) mipDimension = Math.max(mipDimension, depth);
            var maximumMipCount = 1;
            while (mipDimension > 1) {
                mipDimension = Math.floor(mipDimension / 2);
                maximumMipCount++;
            }
            if (mipCount > maximumMipCount) return this.error.unsupportedDimension;

            var formatInfo = this.describeFormat(format);
            if (!formatInfo) return this.error.unsupportedFormat;
            if (!this.supportsFormatDimension(format, dimension))
                return this.error.unsupportedFormat;
            if (!this.supportsShape(dimension, width, height, depth, layers))
                return this.error.unsupportedDimension;
            if ((flags & 30) !== 0) return this.error.unsupportedTexture;
            if (formatInfo.powerOfTwo &&
                ((width & (width - 1)) !== 0 || (height & (height - 1)) !== 0))
                return this.error.unsupportedTexture;
            if (formatInfo.topMipBlockMultiple &&
                (width % formatInfo.blockWidth !== 0 ||
                    height % formatInfo.blockHeight !== 0))
                return this.error.unsupportedTexture;
            if (textureHigh !== 0) return this.error.invalidArgument;

            var texture = GL.textures[textureId];
            if (!texture) return this.error.unsupportedTexture;
            if (typeof this.context.isTexture === 'function' && !this.context.isTexture(texture))
                return this.error.targetStale;
            return this.error.none;
        },

        metadataMatches: function(target, pointer) {
            return target.width === HEAPU32[(pointer + 12) >> 2] &&
                target.height === HEAPU32[(pointer + 16) >> 2] &&
                target.depth === HEAPU32[(pointer + 20) >> 2] &&
                target.layers === HEAPU32[(pointer + 24) >> 2] &&
                target.mipCount === HEAPU32[(pointer + 28) >> 2] &&
                target.format === HEAPU32[(pointer + 32) >> 2] &&
                target.dimension === HEAPU32[(pointer + 36) >> 2] &&
                target.sampleCount === HEAPU32[(pointer + 40) >> 2] &&
                target.flags === HEAPU32[(pointer + 44) >> 2];
        },

        resolveBatchTarget: function(pointer) {
            var low = HEAPU32[pointer >> 2];
            var high = HEAPU32[(pointer + 4) >> 2];
            var target = low === 0 ? null : this.targets[low];
            if (!target || target.tokenHigh !== high) {
                this.resolveError = this.error.targetNotFound;
                return null;
            }
            if (target.state !== 1) {
                this.resolveError = this.error.targetClosing;
                return null;
            }
            if (target.generation !== HEAPU32[(pointer + 8) >> 2] ||
                !this.metadataMatches(target, pointer) || !GL.textures[target.textureId] ||
                GL.textures[target.textureId] !== target.texture ||
                (typeof this.context.isTexture === 'function' && !this.context.isTexture(target.texture))) {
                this.resolveError = this.error.targetStale;
                return null;
            }
            this.resolveError = this.error.none;
            return target;
        },

        terminal: function(batch, error, failedRegion, detail, state, stage) {
            HEAP32[(batch + 72) >> 2] = error;
            HEAP32[(batch + 76) >> 2] = failedRegion;
            HEAPU32[(batch + 80) >> 2] = stage >>> 0;
            HEAPU32[(batch + 84) >> 2] = detail >>> 0;
            HEAPU32[(batch + 88) >> 2] = state;
            return error;
        },

        reject: function(batch, error, failedRegion, detail, state, stage) {
            this.addCounter('rejected', 1);
            return this.terminal(batch, error, failedRegion, detail, state, stage);
        },

        validateRegion: function(pointer, target, writtenBytes) {
            if (HEAPU32[(pointer + 8) >> 2] !== 0 ||
                (HEAPU32[(pointer + 60) >> 2] & ~this.regionPublicationMarker) !== 0)
                return this.error.invalidLayout;

            var mip = HEAPU32[(pointer + 4) >> 2];
            var x = HEAP32[(pointer + 12) >> 2];
            var y = HEAP32[(pointer + 16) >> 2];
            var z = HEAP32[(pointer + 20) >> 2];
            var width = HEAPU32[(pointer + 24) >> 2];
            var height = HEAPU32[(pointer + 28) >> 2];
            var depth = HEAPU32[(pointer + 32) >> 2];
            var baseLayer = HEAPU32[(pointer + 36) >> 2];
            var layerCount = HEAPU32[(pointer + 40) >> 2];
            if (mip >= target.mipCount || x < 0 || y < 0 || z < 0 || width === 0 ||
                height === 0 || depth === 0 || layerCount === 0 || width > 0x7fffffff ||
                height > 0x7fffffff || depth > 0x7fffffff || layerCount > 0x7fffffff)
                return this.error.invalidLayout;

            var divisor = Math.pow(2, mip);
            var mipWidth = Math.max(1, Math.floor(target.width / divisor));
            var mipHeight = Math.max(1, Math.floor(target.height / divisor));
            var mipDepth = Math.max(1, Math.floor(target.depth / divisor));
            if (width > mipWidth - x || height > mipHeight - y)
                return this.error.invalidLayout;
            if (target.dimension === 1 && (z !== 0 || depth !== 1 || baseLayer !== 0 || layerCount !== 1))
                return this.error.invalidLayout;
            if (target.dimension === 2 && (z !== 0 || depth !== 1 || baseLayer >= target.layers ||
                layerCount > target.layers - baseLayer)) return this.error.invalidLayout;
            if (target.dimension === 3 && (baseLayer !== 0 || layerCount !== 1 ||
                z >= mipDepth || depth > mipDepth - z)) return this.error.invalidLayout;
            if (target.dimension === 4 && (z !== 0 || depth !== 1 || baseLayer >= 6 ||
                layerCount > 6 - baseLayer)) return this.error.invalidLayout;

            var info = target.formatInfo;
            if (info.compressed) {
                if (x % info.blockWidth !== 0 || y % info.blockHeight !== 0 ||
                    z % info.blockDepth !== 0 ||
                    (width % info.blockWidth !== 0 && x + width !== mipWidth) ||
                    (height % info.blockHeight !== 0 && y + height !== mipHeight) ||
                    (depth % info.blockDepth !== 0 && z + depth !== mipDepth))
                    return this.error.invalidLayout;
                if (info.wholeMipOnly && (x !== 0 || y !== 0 || z !== 0 ||
                    width !== mipWidth || height !== mipHeight || depth !== mipDepth))
                    return this.error.invalidLayout;
            }
            var layout = this.regionLayout(info, width, height, depth, layerCount);
            if (!layout) return this.error.sourceOutOfRange;
            var rowPitch = HEAPU32[(pointer + 48) >> 2] || layout.rowBytes;
            if (rowPitch < layout.rowBytes)
                return this.error.sourceOutOfRange;

            var slotOffset = HEAPU32[(pointer + 44) >> 2];
            if (slotOffset > writtenBytes)
                return this.error.sourceOutOfRange;
            var available = writtenBytes - slotOffset;
            if (available < layout.rowBytes)
                return this.error.sourceOutOfRange;

            var span = layout.rowBytes;
            if (layout.blockRows > 1) {
                if (rowPitch > Math.floor((available - layout.rowBytes) /
                    (layout.blockRows - 1)))
                    return this.error.sourceOutOfRange;
                span += (layout.blockRows - 1) * rowPitch;
            }
            if (rowPitch > Math.floor(0xffffffff / layout.blockRows))
                return this.error.sourceOutOfRange;
            var minimumImagePitch = rowPitch * layout.blockRows;
            var imagePitch = HEAPU32[(pointer + 52) >> 2] || minimumImagePitch;
            if (imagePitch < minimumImagePitch)
                return this.error.sourceOutOfRange;
            if (layout.blocksZ > 1) {
                if (imagePitch > Math.floor((available - span) / (layout.blocksZ - 1)))
                    return this.error.sourceOutOfRange;
                span += (layout.blocksZ - 1) * imagePitch;
            }
            if (imagePitch > Math.floor(0xffffffff / layout.blocksZ))
                return this.error.sourceOutOfRange;
            var minimumLayerPitch = imagePitch * layout.blocksZ;
            var layerPitch = HEAPU32[(pointer + 56) >> 2] || minimumLayerPitch;
            if (layerPitch < minimumLayerPitch)
                return this.error.sourceOutOfRange;
            if (layerCount > 1) {
                if (layerPitch > Math.floor((available - span) / (layerCount - 1)))
                    return this.error.sourceOutOfRange;
                span += (layerCount - 1) * layerPitch;
            }
            return span <= available ? this.error.none : this.error.sourceOutOfRange;
        },

        isSingleCommandPublicationMarker: function(pointer, target) {
            if (HEAPU32[(pointer + 32) >> 2] !== 1 ||
                HEAPU32[(pointer + 40) >> 2] !== 1)
                return false;
            var layout = this.regionLayout(target.formatInfo,
                HEAPU32[(pointer + 24) >> 2], HEAPU32[(pointer + 28) >> 2], 1, 1);
            if (!layout) return false;
            var explicitRowPitch = HEAPU32[(pointer + 48) >> 2];
            var explicitImagePitch = HEAPU32[(pointer + 52) >> 2];
            var explicitLayerPitch = HEAPU32[(pointer + 56) >> 2];
            return (explicitRowPitch === 0 || explicitRowPitch === layout.rowBytes) &&
                (explicitImagePitch === 0 || explicitImagePitch === layout.imageBytes) &&
                (explicitLayerPitch === 0 || explicitLayerPitch === layout.layerBytes);
        }
    },

    ls_gpu_v6_get_device_info__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_get_device_info: function(info) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(info, 64, 8)) return state.error.invalidArgument;
        state.clearBytes(info, 64);
        try {
            var contextError = state.ensureContext();
            if (contextError !== 0) return contextError;
            HEAPU32[info >> 2] = 64;
            HEAPU16[(info + 4) >> 1] = state.abiMajor;
            HEAPU16[(info + 6) >> 1] = state.abiMinor;
            HEAPU32[(info + 8) >> 2] = 11;
            HEAPU32[(info + 12) >> 2] = 178;
            HEAP32[(info + 16) >> 2] = -1;
            HEAPU32[(info + 20) >> 2] = 0;
            HEAPU32[(info + 24) >> 2] = state.epochLow;
            HEAPU32[(info + 28) >> 2] = state.epochHigh;
            HEAPU32[(info + 32) >> 2] = 143;
            HEAPU32[(info + 36) >> 2] = 6;
            HEAPU32[(info + 40) >> 2] = state.maxSubmissionStagingBytes;
            HEAPU32[(info + 44) >> 2] = 0;
            HEAPU32[(info + 48) >> 2] = 1;
            HEAPU32[(info + 52) >> 2] = state.contractFingerprint;
            HEAPU32[(info + 56) >> 2] = state.slotBytes;
            HEAPU32[(info + 60) >> 2] = state.maxRegionsPerSubmission;
            return state.error.none;
        } catch (exception) {
            state.clearBytes(info, 64);
            return state.exceptionError();
        }
    },

    ls_gpu_v6_query_support__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_query_support: function(query, queryBytes, info, outBytes) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(query, 48, 4) || (queryBytes >>> 0) < 48 ||
            !state.fits(info, 64, 4) || (outBytes >>> 0) < 64)
            return state.error.invalidArgument;
        var queryStructSize = HEAPU32[query >> 2];
        var format = HEAPU32[(query + 4) >> 2];
        var dimension = HEAPU32[(query + 8) >> 2];
        var resourceKind = HEAPU32[(query + 12) >> 2];
        var aspect = HEAPU32[(query + 16) >> 2];
        var width = HEAPU32[(query + 20) >> 2];
        var height = HEAPU32[(query + 24) >> 2];
        var depth = HEAPU32[(query + 28) >> 2];
        var layers = HEAPU32[(query + 32) >> 2];
        var mipCount = HEAPU32[(query + 36) >> 2];
        var sampleCount = HEAPU32[(query + 40) >> 2];
        var reserved = HEAPU32[(query + 44) >> 2];
        state.clearBytes(info, 64);
        HEAPU32[info >> 2] = 64;
        try {
            if (queryStructSize !== 48 || format === 0 || format >= 143 ||
                dimension === 0 || dimension >= 6 || resourceKind > 1 || aspect > 2 ||
                sampleCount !== 1 || reserved !== 0)
                return state.error.invalidArgument;
            var layout = state.logicalFormatLayout(format, aspect);
            if (!layout || (resourceKind === 1) !== layout.depthStencil)
                return state.error.invalidArgument;
            var formatLevel = width === 0 && height === 0 && depth === 0 &&
                layers === 0 && mipCount === 0;
            if (!formatLevel) {
                if (width === 0 || height === 0 || depth === 0 || layers === 0 ||
                    mipCount === 0)
                    return state.error.invalidArgument;
                if ((dimension === 1 && (depth !== 1 || layers !== 1)) ||
                    (dimension === 2 && depth !== 1) ||
                    (dimension === 3 && layers !== 1) ||
                    (dimension === 4 && (depth !== 1 || width !== height || layers !== 6)) ||
                    (dimension === 5 && (depth !== 1 || width !== height || layers < 6 ||
                        layers % 6 !== 0)))
                    return state.error.unsupportedDimension;
                var mipDimension = Math.max(width, height);
                if (dimension === 3) mipDimension = Math.max(mipDimension, depth);
                var maximumMipCount = 1;
                while (mipDimension > 1) {
                    mipDimension = Math.floor(mipDimension / 2);
                    maximumMipCount++;
                }
                if (mipCount > maximumMipCount)
                    return state.error.unsupportedDimension;
                if (layout.pvrtc && ((width & (width - 1)) !== 0 ||
                    (height & (height - 1)) !== 0))
                    return state.error.unsupportedTexture;
                if ((layout.pvrtc || format >= 84 && format <= 97) &&
                    (width % layout.blockWidth !== 0 ||
                        height % layout.blockHeight !== 0))
                    return state.error.unsupportedTexture;
            }
            HEAPU32[(info + 8) >> 2] = state.supportEdgeRemainder |
                state.supportExplicitPitches | state.supportPaddedRowRepack |
                (layout.pvrtc ? state.supportWholeMipOnly | state.supportPowerOfTwo |
                    state.supportTopMipBlockMultiple : state.supportRegional) |
                (format >= 84 && format <= 97 ? state.supportTopMipBlockMultiple : 0);
            HEAPU32[(info + 12) >> 2] = layout.supportedAspects;
            HEAPU32[(info + 16) >> 2] = layout.blockWidth;
            HEAPU32[(info + 20) >> 2] = layout.blockHeight;
            HEAPU32[(info + 24) >> 2] = layout.blockDepth;
            HEAPU32[(info + 28) >> 2] = layout.bytesPerBlock;
            HEAPU32[(info + 32) >> 2] = layout.minimumBlocksX;
            HEAPU32[(info + 36) >> 2] = layout.minimumBlocksY;
            HEAPU32[(info + 40) >> 2] = layout.minimumBlocksZ;
            HEAPU32[(info + 44) >> 2] = 1;
            HEAPU32[(info + 48) >> 2] = 1;
            var contextError = state.ensureContext();
            if (contextError !== 0) return contextError;
            var supported = resourceKind === 0 && aspect === 0 && dimension <= 4 &&
                state.supportsFormatDimension(format, dimension) &&
                state.supportsShape(dimension, width, height, depth, layers);
            HEAPU32[(info + 4) >> 2] = supported ? 1 : 0;
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_register_target__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_register_target: function(registration, handle) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(handle, 16, 8)) return state.error.invalidArgument;
        state.clearBytes(handle, 16);
        var createdToken = 0;
        try {
            var contextError = state.ensureContext();
            if (contextError !== 0) return contextError;
            var validation = state.validateRegistration(registration);
            if (validation !== 0) return validation;
            if (state.nextToken === 0xffffffff) return state.error.internal;

            var textureId = HEAPU32[(registration + 16) >> 2];
            var texture = GL.textures[textureId];
            for (var existingToken in state.targets) {
                if (!Object.prototype.hasOwnProperty.call(state.targets, existingToken)) continue;
                var existing = state.targets[existingToken];
                if ((existing.state === 1 || existing.state === 2) &&
                    existing.texture === texture)
                    return state.error.targetBusy;
            }
            var tokenLow = state.nextToken++;
            var target = {
                tokenLow: tokenLow,
                tokenHigh: state.tokenHigh,
                generation: 1,
                state: 1,
                leases: 0,
                epochLow: state.epochLow,
                epochHigh: state.epochHigh,
                textureId: textureId,
                texture: texture,
                width: HEAPU32[(registration + 24) >> 2],
                height: HEAPU32[(registration + 28) >> 2],
                depth: HEAPU32[(registration + 32) >> 2],
                layers: HEAPU32[(registration + 36) >> 2],
                mipCount: HEAPU32[(registration + 40) >> 2],
                format: HEAPU32[(registration + 44) >> 2],
                dimension: HEAPU32[(registration + 48) >> 2],
                sampleCount: HEAPU32[(registration + 52) >> 2],
                flags: HEAPU32[(registration + 4) >> 2]
            };
            target.formatInfo = state.describeFormat(target.format);
            target.bindingBit = state.bindingBitForDimension(target.dimension);
            state.targets[tokenLow] = target;
            createdToken = tokenLow;
            state.writeHandle(handle, target);
            createdToken = 0;
            return state.error.none;
        } catch (exception) {
            if (createdToken !== 0) delete state.targets[createdToken];
            state.clearBytes(handle, 16);
            return state.exceptionError();
        }
    },

    ls_gpu_v6_refresh_target__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_refresh_target: function(registration, handle) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(handle, 16, 8)) return state.error.invalidArgument;
        try {
            var contextError = state.ensureContext();
            if (contextError !== 0) return contextError;
            var target = state.targetForHandle(handle);
            if (!target) return state.error.targetNotFound;
            if (target.state !== 1) return state.error.targetClosing;
            if (target.generation !== HEAPU32[(handle + 8) >> 2]) return state.error.targetStale;
            var validation = state.validateRegistration(registration);
            if (validation !== 0) return validation;
            if (target.leases !== 0) return state.error.targetBusy;
            if (target.generation === 0xffffffff) return state.error.internal;

            var textureId = HEAPU32[(registration + 16) >> 2];
            var texture = GL.textures[textureId];
            for (var existingToken in state.targets) {
                if (!Object.prototype.hasOwnProperty.call(state.targets, existingToken)) continue;
                var existing = state.targets[existingToken];
                if (existing !== target && (existing.state === 1 || existing.state === 2) &&
                    existing.texture === texture)
                    return state.error.targetBusy;
            }
            var refreshedFormat = HEAPU32[(registration + 44) >> 2];
            var refreshedDimension = HEAPU32[(registration + 48) >> 2];
            var refreshedFormatInfo = state.describeFormat(refreshedFormat);
            var refreshedBindingBit = state.bindingBitForDimension(refreshedDimension);
            target.generation++;
            target.textureId = textureId;
            target.texture = texture;
            target.width = HEAPU32[(registration + 24) >> 2];
            target.height = HEAPU32[(registration + 28) >> 2];
            target.depth = HEAPU32[(registration + 32) >> 2];
            target.layers = HEAPU32[(registration + 36) >> 2];
            target.mipCount = HEAPU32[(registration + 40) >> 2];
            target.format = refreshedFormat;
            target.dimension = refreshedDimension;
            target.sampleCount = HEAPU32[(registration + 52) >> 2];
            target.flags = HEAPU32[(registration + 4) >> 2];
            target.formatInfo = refreshedFormatInfo;
            target.bindingBit = refreshedBindingBit;
            state.writeHandle(handle, target);
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_unregister_target__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_unregister_target: function(handle) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(handle, 16, 8)) return state.error.invalidArgument;
        try {
            var contextError = state.ensureContext();
            if (contextError !== 0) return contextError;
            var target = state.targetForHandle(handle);
            if (!target) return state.error.targetNotFound;
            if (target.generation !== HEAPU32[(handle + 8) >> 2]) return state.error.targetStale;
            if (target.state === 3) return state.error.none;
            target.state = 2;
            state.retireTargetIfPossible(target);
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_get_target_status__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_get_target_status: function(handle, status) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(status, 32, 8)) return state.error.invalidArgument;
        state.clearBytes(status, 32);
        if (!state.fits(handle, 16, 8)) return state.error.invalidArgument;
        try {
            var target = state.targetForHandle(handle);
            var error = target ? (target.generation === HEAPU32[(handle + 8) >> 2] ? 0 : state.error.targetStale) :
                state.error.targetNotFound;
            HEAPU32[status >> 2] = 32;
            HEAPU32[(status + 4) >> 2] = target ? target.state : 0;
            HEAPU32[(status + 8) >> 2] = target ? target.generation : 0;
            HEAPU32[(status + 12) >> 2] = target ? target.leases : 0;
            HEAPU32[(status + 16) >> 2] = target ? target.epochLow : state.epochLow;
            HEAPU32[(status + 20) >> 2] = target ? target.epochHigh : state.epochHigh;
            HEAP32[(status + 24) >> 2] = error;
            HEAPU32[(status + 28) >> 2] = 0;
            if (error === state.error.none && target && target.state === 3)
                delete state.targets[target.tokenLow];
            return error;
        } catch (exception) {
            state.clearBytes(status, 32);
            return state.exceptionError();
        }
    },

    ls_gpu_v6_acquire_slot__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_acquire_slot: function(minBytes, slotId, slotGeneration, capacityBytes, mapped) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(slotId, 4, 4) || !state.fits(slotGeneration, 4, 4) ||
            !state.fits(capacityBytes, 4, 4) || !state.fits(mapped, 4, 4))
            return state.error.invalidArgument;
        HEAPU32[slotId >> 2] = 0;
        HEAPU32[slotGeneration >> 2] = 0;
        HEAPU32[capacityBytes >> 2] = 0;
        HEAPU32[mapped >> 2] = 0;
        try {
            var contextError = state.ensureContext();
            if (contextError !== 0) return contextError;
            minBytes = minBytes >>> 0;
            if (minBytes > state.maxSubmissionStagingBytes)
                return state.error.invalidArgument;
            var slot = null;
            if (minBytes <= state.slotBytes) {
                var standardCount = 0;
                for (var index = 0; index < state.slots.length; index++) {
                    var candidate = state.slots[index];
                    if (candidate.transient) continue;
                    standardCount++;
                    if (candidate.state === 0 && !slot) slot = candidate;
                }
                if (!slot) {
                    if (standardCount >= state.maxResidentSlots) {
                        state.addCounter('backpressure', 1);
                        return state.error.backpressure;
                    }
                    if (state.nextSlotId === 0xffffffff) return state.error.internal;
                    var base = _malloc(state.slotBytes);
                    if (base === 0) return state.error.outOfMemory;
                    slot = { id: state.nextSlotId++, generation: 1, state: 0,
                        capacity: state.slotBytes, base: base, transient: false };
                    state.slots.push(slot);
                }
            } else {
                if (state.nextSlotId === 0xffffffff) return state.error.internal;
                var transientBase = _malloc(minBytes);
                if (transientBase === 0) return state.error.outOfMemory;
                slot = { id: state.nextSlotId++, generation: 1, state: 0,
                    capacity: minBytes, base: transientBase, transient: true };
                state.slots.push(slot);
            }
            slot.state = 1;
            HEAPU32[slotId >> 2] = slot.id;
            HEAPU32[slotGeneration >> 2] = slot.generation;
            HEAPU32[capacityBytes >> 2] = slot.capacity;
            HEAPU32[mapped >> 2] = slot.base;
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_release_slot__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_release_slot: function(slotId, slotGeneration) {
        var state = LightSideGpuUploadV6;
        try {
            var slot = state.findSlot(slotId >>> 0);
            if (!slot || slot.generation !== (slotGeneration >>> 0))
                return state.error.none;
            if (slot.state === 2) return state.error.slotBusy;
            state.freeSlot(slot);
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_create_sequence__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_create_sequence: function(descriptor, serial) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(descriptor, 24, 8) || !state.fits(serial, 8, 8))
            return state.error.invalidArgument;
        HEAPU32[serial >> 2] = 0;
        HEAPU32[(serial + 4) >> 2] = 0;
        try {
            var contextError = state.ensureContext();
            if (contextError !== state.error.none) return contextError;
            var flags = HEAPU32[(descriptor + 4) >> 2];
            if (HEAPU32[descriptor >> 2] !== 24 || (flags & ~1) !== 0 ||
                HEAPU32[(descriptor + 8) >> 2] !== state.epochLow ||
                HEAPU32[(descriptor + 12) >> 2] !== state.epochHigh ||
                HEAPU32[(descriptor + 16) >> 2] !== 0 ||
                HEAPU32[(descriptor + 20) >> 2] !== 0)
                return state.error.invalidArgument;
            if (state.sequenceCount >= state.maxSequences) return state.error.backpressure;
            var identity = state.allocateSequenceSerial();
            if (!identity) return state.error.internal;
            var sequence = {
                serialLow: identity.low,
                serialHigh: identity.high,
                epochLow: state.epochLow,
                epochHigh: state.epochHigh,
                configurationFlags: flags,
                state: 1,
                gpuState: 0,
                contentState: 0,
                publicationState: (flags & 1) !== 0 ? 1 : 0,
                resultCode: 0,
                failedBatch: -1,
                failedRegion: -1,
                backendDetail: 0,
                flags: 0,
                expectedBatchCount: 0,
                admittedBatchCount: 0,
                sourceConsumedBatchCount: 0,
                encodedBatchCount: 0,
                gpuTerminalBatchCount: 0,
                retiredBatchCount: 0,
                targetEntries: [],
                targetLeasesReleased: false
            };
            state.sequences[state.sequenceKey(identity.low, identity.high)] = sequence;
            state.sequenceCount++;
            HEAPU32[serial >> 2] = identity.low;
            HEAPU32[(serial + 4) >> 2] = identity.high;
            return state.error.none;
        } catch (exception) {
            HEAPU32[serial >> 2] = 0;
            HEAPU32[(serial + 4) >> 2] = 0;
            return state.exceptionError();
        }
    },

    ls_gpu_v6_seal_sequence__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_seal_sequence: function(serialLow, serialHigh, expectedBatchCount) {
        var state = LightSideGpuUploadV6;
        try {
            serialLow = serialLow >>> 0;
            serialHigh = serialHigh >>> 0;
            expectedBatchCount = expectedBatchCount >>> 0;
            var sequence = state.sequences[state.sequenceKey(serialLow, serialHigh)];
            if (!sequence) return state.error.sequenceNotFound;
            if (sequence.state !== 1) return state.error.sequenceClosing;
            if (expectedBatchCount === 0 || (sequence.configurationFlags & 1) !== 0 ||
                expectedBatchCount !== sequence.admittedBatchCount)
                return state.error.invalidArgument;
            sequence.expectedBatchCount = expectedBatchCount;
            sequence.state = 5;
            state.releaseSequenceTargetLeases(sequence);
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_abort_sequence__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_abort_sequence: function(serialLow, serialHigh) {
        var state = LightSideGpuUploadV6;
        try {
            var sequence = state.sequences[state.sequenceKey(serialLow, serialHigh)];
            if (!sequence) return state.error.sequenceNotFound;
            return state.abortSequence(sequence);
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_get_sequence_status__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_get_sequence_status: function(serialLow, serialHigh, status) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(status, 80, 8)) return state.error.invalidArgument;
        state.clearBytes(status, 80);
        try {
            var sequence = state.findSequence(serialLow, serialHigh);
            if (!sequence) return state.error.sequenceNotFound;
            state.writeSequenceStatus(status, sequence);
            return state.error.none;
        } catch (exception) {
            state.clearBytes(status, 80);
            return state.exceptionError();
        }
    },

    ls_gpu_v6_close_sequence__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_close_sequence: function(serialLow, serialHigh) {
        var state = LightSideGpuUploadV6;
        try {
            var key = state.sequenceKey(serialLow, serialHigh);
            var sequence = state.sequences[key];
            if (!sequence) return state.error.sequenceNotFound;
            if (sequence.state === 1) {
                var abortError = state.abortSequence(sequence);
                if (abortError !== state.error.none) return abortError;
            }
            sequence.flags |= 1;
            if (state.sequenceHistory.length >= state.sequenceHistoryCapacity)
                state.sequenceHistory.shift();
            state.sequenceHistory.push(sequence);
            delete state.sequences[key];
            state.sequenceCount--;
            return state.error.none;
        } catch (exception) {
            return state.exceptionError();
        }
    },

    ls_gpu_v6_create_submission__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_create_submission: function(batch, suppliedBytes) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(batch, 96, 8)) return state.error.invalidArgument;
        var accepted = false;
        var sequence = null;
        var slot = null;
        var publicationTerminal = false;
        var admissionStage = state.stageLayout;
        try {
        var contextError = state.ensureContext();
        if (contextError !== 0)
            return state.reject(batch, contextError, -1, 0,
                contextError === state.error.contextLost ? 5 : 3, state.stageLayout);

        suppliedBytes = suppliedBytes >>> 0;
        var totalBytes = HEAPU32[(batch + 8) >> 2];
        var sequenceLow = HEAPU32[(batch + 32) >> 2];
        var sequenceHigh = HEAPU32[(batch + 36) >> 2];
        var sequenceOrdinal = HEAPU32[(batch + 40) >> 2];
        var targetOffset = HEAPU32[(batch + 56) >> 2];
        var targetCount = HEAPU32[(batch + 60) >> 2];
        var regionOffset = HEAPU32[(batch + 64) >> 2];
        var regionCount = HEAPU32[(batch + 68) >> 2];
        var batchFlags = HEAPU32[(batch + 12) >> 2];
        var targetEnd = 96 + targetCount * 48;
        if (HEAPU32[batch >> 2] !== state.magic ||
            HEAPU16[(batch + 4) >> 1] !== state.abiMajor ||
            HEAPU16[(batch + 6) >> 1] !== state.abiMinor)
            return state.reject(batch, state.error.abiMismatch, -1, 0, 3, state.stageLayout);
        var overlapFlags = state.batchAssumeNonOverlapping |
            state.batchOrderedOverlaps;
        var knownBatchFlags = overlapFlags | state.batchObserveSharedGlErrors;
        if (totalBytes !== suppliedBytes || totalBytes < 96 || !state.fits(batch, totalBytes, 8) ||
            (batchFlags & ~knownBatchFlags) !== 0 ||
            (batchFlags & overlapFlags) === overlapFlags ||
            targetCount === 0 || regionCount === 0 ||
            regionCount > state.maxRegionsPerSubmission ||
            targetOffset !== 96 || targetCount > Math.floor((totalBytes - 96) / 48) ||
            regionOffset < targetEnd || (regionOffset & 7) !== 0 ||
            regionCount > Math.floor((totalBytes - regionOffset) / 64) ||
            totalBytes !== regionOffset + regionCount * 64 ||
            (HEAPU32[(batch + 16) >> 2] === 0 && HEAPU32[(batch + 20) >> 2] === 0) ||
            (sequenceLow === 0 && sequenceHigh === 0 && sequenceOrdinal !== 0) ||
            HEAP32[(batch + 72) >> 2] !== 0 || HEAP32[(batch + 76) >> 2] !== -1 ||
            HEAPU32[(batch + 80) >> 2] !== 0 || HEAPU32[(batch + 84) >> 2] !== 0 ||
            HEAPU32[(batch + 88) >> 2] !== 0 || HEAPU32[(batch + 92) >> 2] !== 0)
            return state.reject(batch, state.error.invalidLayout, -1, 0, 3, state.stageLayout);
        for (var paddingOffset = targetEnd; paddingOffset < regionOffset; paddingOffset++)
            if (HEAPU8[batch + paddingOffset] !== 0)
                return state.reject(batch, state.error.invalidLayout, -1, 0, 3, state.stageLayout);

        admissionStage = state.stageSlot;
        var slotIdentity = HEAPU32[(batch + 44) >> 2];
        var slotGeneration = HEAPU32[(batch + 48) >> 2];
        var writtenBytes = HEAPU32[(batch + 52) >> 2];
        slot = state.findSlot(slotIdentity);
        if (!slot || slot.generation !== slotGeneration) {
            slot = null;
            return state.reject(batch, state.error.slotNotFound, -1, 0, 3, state.stageSlot);
        }
        if (slot.state !== 1)
            return state.reject(batch, state.error.slotBusy, -1, 0, 3, state.stageSlot);
        if (writtenBytes > slot.capacity)
            return state.reject(batch, state.error.sourceOutOfRange, -1, 0, 3, state.stageSlot);
        if (HEAPU32[(batch + 24) >> 2] !== state.epochLow ||
            HEAPU32[(batch + 28) >> 2] !== state.epochHigh)
            return state.reject(batch, state.error.targetStale, -1, 0, 3, state.stageSlot);

        if (sequenceLow !== 0 || sequenceHigh !== 0) {
            sequence = state.sequences[state.sequenceKey(sequenceLow, sequenceHigh)];
            if (!sequence)
                return state.reject(batch, state.error.sequenceNotFound, -1, 0, 3, state.stageLayout);
            if (sequence.state !== 1)
                return state.reject(batch, state.error.sequenceClosing, -1, 0, 3, state.stageLayout);
            if (sequenceOrdinal !== sequence.admittedBatchCount)
                return state.reject(batch, state.error.sequenceOrder, -1, 0, 3, state.stageLayout);
            if (sequence.admittedBatchCount === 0xffffffff)
                return state.reject(batch, state.error.backpressure, -1, 0, 3, state.stageLayout);
        }

        admissionStage = state.stageTarget;
        var bindingsMask = 0;
        var batchTargets = sequence ? [] : null;
        var serialLow = HEAPU32[(batch + 16) >> 2];
        var serialHigh = HEAPU32[(batch + 20) >> 2];
        for (var targetIndex = 0; targetIndex < targetCount; targetIndex++) {
            var targetPointer = batch + targetOffset + targetIndex * 48;
            if ((HEAPU32[targetPointer >> 2] === 0 &&
                HEAPU32[(targetPointer + 4) >> 2] === 0) ||
                (HEAPU32[(targetPointer + 44) >> 2] & ~31) !== 0)
                return state.reject(batch, state.error.invalidLayout, -1, 0, 3, state.stageTarget);
            var resolved = state.resolveBatchTarget(targetPointer);
            if (!resolved)
                return state.reject(batch, state.resolveError, -1, 0, 3, state.stageTarget);
            if (resolved.lastBatchSerialLow === serialLow &&
                resolved.lastBatchSerialHigh === serialHigh)
                return state.reject(batch, state.error.invalidLayout, -1, 0, 3, state.stageTarget);
            resolved.lastBatchSerialLow = serialLow;
            resolved.lastBatchSerialHigh = serialHigh;
            bindingsMask |= resolved.bindingBit;
            if (sequence) batchTargets.push(resolved);
        }

        admissionStage = state.stageRegion;
        var payloadBytes = 0;
        var publicationMarker = false;
        var requiredScratchBytes = 0;
        for (var regionIndex = 0; regionIndex < regionCount; regionIndex++) {
            var regionPointer = batch + regionOffset + regionIndex * 64;
            var regionTargetIndex = HEAPU32[regionPointer >> 2];
            if (regionTargetIndex >= targetCount)
                return state.reject(batch, state.error.invalidLayout, regionIndex, 0, 3, state.stageRegion);
            var regionTargetPointer = batch + targetOffset + regionTargetIndex * 48;
            var regionTarget = state.targets[HEAPU32[regionTargetPointer >> 2]];
            if (!regionTarget)
                return state.reject(batch, state.error.targetStale, regionIndex, 0, 3, state.stageRegion);
            var regionError = state.validateRegion(regionPointer, regionTarget, writtenBytes);
            if (regionError !== 0)
                return state.reject(batch, regionError, regionIndex, 0, 3, state.stageRegion);
            var regionFlags = HEAPU32[(regionPointer + 60) >> 2];
            if ((regionFlags & state.regionPublicationMarker) !== 0) {
                if (publicationMarker || regionIndex + 1 !== regionCount ||
                    !state.isSingleCommandPublicationMarker(regionPointer, regionTarget))
                    return state.reject(batch, state.error.invalidLayout, regionIndex, 0, 3, state.stageMarker);
                publicationMarker = true;
            }
            if ((batchFlags & overlapFlags) === 0) {
                for (var previousIndex = 0; previousIndex < regionIndex; previousIndex++) {
                    var previousPointer = batch + regionOffset + previousIndex * 64;
                    if (HEAPU32[previousPointer >> 2] === regionTargetIndex &&
                        HEAPU32[(previousPointer + 4) >> 2] === HEAPU32[(regionPointer + 4) >> 2] &&
                        state.regionsOverlap(previousPointer, regionPointer, regionTarget.dimension))
                        return state.reject(batch, state.error.invalidLayout, regionIndex, 0, 3, state.stageOverlap);
                }
            }

            var regionInfo = regionTarget.formatInfo;
            var regionWidth = HEAPU32[(regionPointer + 24) >> 2];
            var regionHeight = HEAPU32[(regionPointer + 28) >> 2];
            var regionDepth = HEAPU32[(regionPointer + 32) >> 2];
            var regionLayers = HEAPU32[(regionPointer + 40) >> 2];
            var regionLayout = state.regionLayout(regionInfo, regionWidth, regionHeight,
                regionDepth, regionLayers);
            if (!regionLayout)
                return state.reject(batch, state.error.invalidLayout, regionIndex, 0, 3, state.stageRegion);
            var regionRowPitch = HEAPU32[(regionPointer + 48) >> 2] ||
                regionLayout.rowBytes;
            var regionImagePitch = HEAPU32[(regionPointer + 52) >> 2] ||
                regionRowPitch * regionLayout.blockRows;
            var regionLayerPitch = HEAPU32[(regionPointer + 56) >> 2] ||
                regionImagePitch * regionLayout.blocksZ;
            if (state.requiresRepack(regionInfo,
                slot.base + HEAPU32[(regionPointer + 44) >> 2], regionRowPitch, regionImagePitch,
                regionLayerPitch, regionLayout, regionDepth, regionLayers))
                requiredScratchBytes = Math.max(requiredScratchBytes, regionLayout.totalBytes);
            payloadBytes += regionLayout.totalBytes;
        }

        if (sequence) {
            if (publicationMarker) {
                if ((sequence.configurationFlags & 1) === 0 || targetCount !== 1 ||
                    regionCount !== 1 || state.sequenceHasTarget(sequence, batchTargets[0]))
                    return state.reject(batch, state.error.invalidLayout, regionCount - 1, 0, 3, state.stageMarker);
                publicationTerminal = true;
            }
        }
        admissionStage = state.stageBackend;
        if (requiredScratchBytes !== 0) {
            try {
                state.ensureScratch(requiredScratchBytes);
            } catch (allocationException) {
                return state.reject(batch, state.error.outOfMemory, -1, 0, 3, state.stageBackend);
            }
        }
        accepted = true;
        slot.state = 2;
        state.addCounter('accepted', 1);
        if (sequence) {
            sequence.admittedBatchCount++;
            if (publicationTerminal) {
                sequence.expectedBatchCount = sequence.admittedBatchCount;
                sequence.state = 2;
            }
            for (var sequenceTargetIndex = 0;
                sequenceTargetIndex < batchTargets.length; sequenceTargetIndex++) {
                var sequenceTarget = batchTargets[sequenceTargetIndex];
                if (state.sequenceHasTarget(sequence, sequenceTarget)) continue;
                sequence.targetEntries.push(sequenceTarget);
                sequenceTarget.leases++;
            }
        }

        var gl = state.context;
        var observeErrors = publicationMarker ||
            (batchFlags & state.batchObserveSharedGlErrors) !== 0 ||
            sequence && (sequence.configurationFlags & 1) !== 0;
        if (observeErrors) {
            // Pre-existing errors in the shared queue belong to other code: drained and
            // discarded, never attributed to this batch; only an unusable channel aborts.
            var foreignErrors = state.drainErrors(gl);
            if (foreignErrors.contextLost || foreignErrors.threw || !foreignErrors.complete) {
                state.freeSlot(slot);
                if (foreignErrors.contextLost) {
                    state.markContextLost();
                    state.finishSequenceBatch(sequence, publicationTerminal,
                        state.error.contextLost, -1, foreignErrors.first, 0);
                    return state.terminal(batch, state.error.contextLost, -1,
                        foreignErrors.first, 5, 0);
                }
                state.finishSequenceBatch(sequence, publicationTerminal,
                    state.error.backendFailed, -1, foreignErrors.first, 0);
                return state.terminal(batch, state.error.backendFailed, -1,
                    foreignErrors.first, 4, 0);
            }
        }
        var previous = {};
        var captured = false;
        var writes = 0;
        var activeRegion = -1;
        var failedRegion = -1;
        var backendDetail = 0;
        var result = state.error.none;
        try {
            if (bindingsMask & 1) previous.texture2D = gl.getParameter(gl.TEXTURE_BINDING_2D);
            if (bindingsMask & 2) previous.texture2DArray = gl.getParameter(gl.TEXTURE_BINDING_2D_ARRAY);
            if (bindingsMask & 4) previous.texture3D = gl.getParameter(gl.TEXTURE_BINDING_3D);
            if (bindingsMask & 8) previous.textureCube = gl.getParameter(gl.TEXTURE_BINDING_CUBE_MAP);
            previous.pbo = gl.getParameter(gl.PIXEL_UNPACK_BUFFER_BINDING);
            previous.alignment = gl.getParameter(gl.UNPACK_ALIGNMENT);
            previous.rowLength = gl.getParameter(gl.UNPACK_ROW_LENGTH);
            previous.imageHeight = gl.getParameter(gl.UNPACK_IMAGE_HEIGHT);
            previous.skipPixels = gl.getParameter(gl.UNPACK_SKIP_PIXELS);
            previous.skipRows = gl.getParameter(gl.UNPACK_SKIP_ROWS);
            previous.skipImages = gl.getParameter(gl.UNPACK_SKIP_IMAGES);
            previous.flipY = gl.getParameter(gl.UNPACK_FLIP_Y_WEBGL);
            previous.premultiply = gl.getParameter(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL);
            previous.colorspace = gl.getParameter(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL);
            captured = true;

            gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, null);
            gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
            gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
            gl.pixelStorei(gl.UNPACK_IMAGE_HEIGHT, 0);
            gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, 0);
            gl.pixelStorei(gl.UNPACK_SKIP_ROWS, 0);
            gl.pixelStorei(gl.UNPACK_SKIP_IMAGES, 0);
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
            gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
            gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, gl.NONE);

            var bound2D = previous.texture2D || null;
            var bound2DArray = previous.texture2DArray || null;
            var bound3D = previous.texture3D || null;
            var boundCube = previous.textureCube || null;
            var currentRowLength = 0;
            var currentImageHeight = 0;
            for (activeRegion = 0; activeRegion < regionCount; activeRegion++) {
                if (publicationMarker && activeRegion === regionCount - 1) {
                    var contentErrors = state.drainErrors(gl);
                    if (state.errorObservationFailed(contentErrors, gl)) {
                        if (backendDetail === 0) backendDetail = contentErrors.first;
                        failedRegion = writes === 0 ? -1 : Math.max(0, activeRegion - 1);
                        if (contentErrors.contextLost) {
                            state.markContextLost();
                            result = state.error.contextLost;
                        } else {
                            result = state.error.backendFailed;
                        }
                        break;
                    }
                }
                var uploadRegion = batch + regionOffset + activeRegion * 64;
                var uploadTargetIndex = HEAPU32[uploadRegion >> 2];
                var uploadTargetPointer = batch + targetOffset + uploadTargetIndex * 48;
                var uploadTarget = state.targets[HEAPU32[uploadTargetPointer >> 2]];
                if (!uploadTarget) throw new Error('target lost after preflight');
                var info = uploadTarget.formatInfo;
                var sourceHeap = state.sourceHeap(info);
                var mip = HEAPU32[(uploadRegion + 4) >> 2];
                var x = HEAP32[(uploadRegion + 12) >> 2];
                var y = HEAP32[(uploadRegion + 16) >> 2];
                var z = HEAP32[(uploadRegion + 20) >> 2];
                var width = HEAPU32[(uploadRegion + 24) >> 2];
                var height = HEAPU32[(uploadRegion + 28) >> 2];
                var depth = HEAPU32[(uploadRegion + 32) >> 2];
                var baseLayer = HEAPU32[(uploadRegion + 36) >> 2];
                var layerCount = HEAPU32[(uploadRegion + 40) >> 2];
                var source = slot.base + HEAPU32[(uploadRegion + 44) >> 2];
                var uploadLayout = state.regionLayout(info, width, height, depth, layerCount);
                if (!uploadLayout) throw new Error('region layout lost after preflight');
                var rowBytes = uploadLayout.rowBytes;
                var rowPitch = HEAPU32[(uploadRegion + 48) >> 2] || rowBytes;
                var imagePitch = HEAPU32[(uploadRegion + 52) >> 2] ||
                    rowPitch * uploadLayout.blockRows;
                var layerPitch = HEAPU32[(uploadRegion + 56) >> 2] ||
                    imagePitch * uploadLayout.blocksZ;
                var repacked = state.requiresRepack(info, source, rowPitch, imagePitch,
                    layerPitch, uploadLayout, depth, layerCount);
                if (repacked) {
                    state.repackRegion(source, rowPitch, imagePitch, layerPitch,
                        uploadLayout, layerCount);
                    source = 0;
                    sourceHeap = state.scratchHeap(info);
                    rowPitch = uploadLayout.rowBytes;
                    imagePitch = uploadLayout.imageBytes;
                    layerPitch = uploadLayout.layerBytes;
                }

                if (info.compressed) {
                    if (currentRowLength !== 0) {
                        gl.pixelStorei(gl.UNPACK_ROW_LENGTH, 0);
                        currentRowLength = 0;
                    }
                    if (currentImageHeight !== 0) {
                        gl.pixelStorei(gl.UNPACK_IMAGE_HEIGHT, 0);
                        currentImageHeight = 0;
                    }
                    if (uploadTarget.dimension === 1) {
                        if (bound2D !== uploadTarget.texture) {
                            gl.bindTexture(gl.TEXTURE_2D, uploadTarget.texture);
                            bound2D = uploadTarget.texture;
                        }
                        writes++;
                        gl.compressedTexSubImage2D(gl.TEXTURE_2D, mip, x, y, width, height,
                            info.internalFormat, sourceHeap, source, uploadLayout.totalBytes);
                    } else if (uploadTarget.dimension === 2) {
                        if (bound2DArray !== uploadTarget.texture) {
                            gl.bindTexture(gl.TEXTURE_2D_ARRAY, uploadTarget.texture);
                            bound2DArray = uploadTarget.texture;
                        }
                        writes++;
                        gl.compressedTexSubImage3D(gl.TEXTURE_2D_ARRAY, mip, x, y, baseLayer,
                            width, height, layerCount, info.internalFormat, sourceHeap, source,
                            uploadLayout.totalBytes);
                    } else if (uploadTarget.dimension === 4) {
                        if (boundCube !== uploadTarget.texture) {
                            gl.bindTexture(gl.TEXTURE_CUBE_MAP, uploadTarget.texture);
                            boundCube = uploadTarget.texture;
                        }
                        for (var compressedFace = 0; compressedFace < layerCount;
                            compressedFace++) {
                            writes++;
                            gl.compressedTexSubImage2D(
                                gl.TEXTURE_CUBE_MAP_POSITIVE_X + baseLayer + compressedFace,
                                mip, x, y, width, height, info.internalFormat, sourceHeap,
                                source + compressedFace * layerPitch, uploadLayout.layerBytes);
                        }
                    } else {
                        throw new Error('unsupported compressed target after preflight');
                    }
                } else {
                var rowLengthExpressible = rowPitch % info.bytesPerPixel === 0;
                var rowLength = !rowLengthExpressible || rowPitch === rowBytes
                    ? 0 : rowPitch / info.bytesPerPixel;
                if (rowLength !== currentRowLength) {
                    gl.pixelStorei(gl.UNPACK_ROW_LENGTH, rowLength);
                    currentRowLength = rowLength;
                }
                var imageHeight = 0;
                if (rowLengthExpressible && uploadTarget.dimension === 2 && layerCount > 1 &&
                    layerPitch % rowPitch === 0 && layerPitch / rowPitch >= height &&
                    layerPitch / rowPitch <= 0x7fffffff)
                    imageHeight = layerPitch / rowPitch;
                else if (rowLengthExpressible && uploadTarget.dimension === 3 && depth > 1 &&
                    imagePitch % rowPitch === 0 && imagePitch / rowPitch >= height &&
                    imagePitch / rowPitch <= 0x7fffffff)
                    imageHeight = imagePitch / rowPitch;
                if (imageHeight !== currentImageHeight) {
                    gl.pixelStorei(gl.UNPACK_IMAGE_HEIGHT, imageHeight);
                    currentImageHeight = imageHeight;
                }
                if (uploadTarget.dimension === 1) {
                    if (bound2D !== uploadTarget.texture) {
                        gl.bindTexture(gl.TEXTURE_2D, uploadTarget.texture);
                        bound2D = uploadTarget.texture;
                    }
                    if (rowLengthExpressible) {
                        writes++;
                        gl.texSubImage2D(gl.TEXTURE_2D, mip, x, y, width, height,
                            info.format, info.type, sourceHeap, source / info.elementBytes);
                    } else {
                        for (var row2D = 0; row2D < height; row2D++) {
                            writes++;
                            gl.texSubImage2D(gl.TEXTURE_2D, mip, x, y + row2D, width, 1,
                                info.format, info.type, sourceHeap,
                                (source + row2D * rowPitch) / info.elementBytes);
                        }
                    }
                } else if (uploadTarget.dimension === 2) {
                    if (bound2DArray !== uploadTarget.texture) {
                        gl.bindTexture(gl.TEXTURE_2D_ARRAY, uploadTarget.texture);
                        bound2DArray = uploadTarget.texture;
                    }
                    if (imageHeight !== 0) {
                        writes++;
                        gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, mip, x, y, baseLayer,
                            width, height, layerCount, info.format, info.type,
                            sourceHeap, source / info.elementBytes);
                    } else {
                        for (var layer = 0; layer < layerCount; layer++) {
                            var layerSource = source + layer * layerPitch;
                            if (rowLengthExpressible) {
                                writes++;
                                gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, mip, x, y, baseLayer + layer,
                                    width, height, 1, info.format, info.type,
                                    sourceHeap, layerSource / info.elementBytes);
                            } else {
                                for (var arrayRow = 0; arrayRow < height; arrayRow++) {
                                    writes++;
                                    gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, mip, x, y + arrayRow,
                                        baseLayer + layer, width, 1, 1, info.format, info.type,
                                        sourceHeap, (layerSource + arrayRow * rowPitch) / info.elementBytes);
                                }
                            }
                        }
                    }
                } else if (uploadTarget.dimension === 3) {
                    if (bound3D !== uploadTarget.texture) {
                        gl.bindTexture(gl.TEXTURE_3D, uploadTarget.texture);
                        bound3D = uploadTarget.texture;
                    }
                    if (imageHeight !== 0) {
                        writes++;
                        gl.texSubImage3D(gl.TEXTURE_3D, mip, x, y, z,
                            width, height, depth, info.format, info.type,
                            sourceHeap, source / info.elementBytes);
                    } else {
                        for (var image = 0; image < depth; image++) {
                            var imageSource = source + image * imagePitch;
                            if (rowLengthExpressible) {
                                writes++;
                                gl.texSubImage3D(gl.TEXTURE_3D, mip, x, y, z + image,
                                    width, height, 1, info.format, info.type,
                                    sourceHeap, imageSource / info.elementBytes);
                            } else {
                                for (var volumeRow = 0; volumeRow < height; volumeRow++) {
                                    writes++;
                                    gl.texSubImage3D(gl.TEXTURE_3D, mip, x, y + volumeRow,
                                        z + image, width, 1, 1, info.format, info.type,
                                        sourceHeap, (imageSource + volumeRow * rowPitch) / info.elementBytes);
                                }
                            }
                        }
                    }
                } else {
                    if (boundCube !== uploadTarget.texture) {
                        gl.bindTexture(gl.TEXTURE_CUBE_MAP, uploadTarget.texture);
                        boundCube = uploadTarget.texture;
                    }
                    for (var face = 0; face < layerCount; face++) {
                        var faceSource = source + face * layerPitch;
                        if (rowLengthExpressible) {
                            writes++;
                            gl.texSubImage2D(gl.TEXTURE_CUBE_MAP_POSITIVE_X + baseLayer + face,
                                mip, x, y, width, height, info.format, info.type,
                                sourceHeap, faceSource / info.elementBytes);
                        } else {
                            for (var cubeRow = 0; cubeRow < height; cubeRow++) {
                                writes++;
                                gl.texSubImage2D(gl.TEXTURE_CUBE_MAP_POSITIVE_X + baseLayer + face,
                                    mip, x, y + cubeRow, width, 1, info.format, info.type,
                                    sourceHeap, (faceSource + cubeRow * rowPitch) / info.elementBytes);
                            }
                        }
                    }
                }
                }
                if (publicationMarker && activeRegion === regionCount - 1) {
                    var markerErrors = state.drainErrors(gl);
                    if (state.errorObservationFailed(markerErrors, gl)) {
                        if (backendDetail === 0) backendDetail = markerErrors.first;
                        failedRegion = activeRegion;
                        if (markerErrors.contextLost) {
                            state.markContextLost();
                            result = state.error.contextLost;
                        } else {
                            result = state.error.backendFailed;
                        }
                    }
                }
            }
            if (result === state.error.none && observeErrors && !publicationMarker) {
                var batchErrors = state.drainErrors(gl);
                if (state.errorObservationFailed(batchErrors, gl)) {
                    if (backendDetail === 0) backendDetail = batchErrors.first;
                    failedRegion = writes === 0 ? -1 : regionCount - 1;
                    if (batchErrors.contextLost) {
                        state.markContextLost();
                        result = state.error.contextLost;
                    } else {
                        result = state.error.backendFailed;
                    }
                }
            }
            var finalContextState = state.safeContextLost(gl);
            if (finalContextState > 0) {
                state.markContextLost();
                result = state.error.contextLost;
                if (writes !== 0 && failedRegion < 0)
                    failedRegion = Math.min(activeRegion, regionCount - 1);
            } else if (finalContextState < 0) {
                result = state.error.internal;
                if (writes !== 0 && failedRegion < 0)
                    failedRegion = Math.min(activeRegion, regionCount - 1);
            }
        } catch (exception) {
            var exceptionContextState = state.safeContextLost(gl);
            if (exceptionContextState > 0) {
                state.markContextLost();
                result = state.error.contextLost;
            } else {
                result = exceptionContextState < 0 ? state.error.internal : state.error.backendFailed;
            }
            if (writes !== 0 && failedRegion < 0)
                failedRegion = Math.min(Math.max(activeRegion, 0), regionCount - 1);
        } finally {
            var restoreFailed = false;
            if (captured) {
                try { gl.bindBuffer(gl.PIXEL_UNPACK_BUFFER, previous.pbo); }
                catch (restoreBufferException) { restoreFailed = true; }
                try { if (bindingsMask & 1) gl.bindTexture(gl.TEXTURE_2D, previous.texture2D); }
                catch (restore2DException) { restoreFailed = true; }
                try { if (bindingsMask & 2) gl.bindTexture(gl.TEXTURE_2D_ARRAY, previous.texture2DArray); }
                catch (restore2DArrayException) { restoreFailed = true; }
                try { if (bindingsMask & 4) gl.bindTexture(gl.TEXTURE_3D, previous.texture3D); }
                catch (restore3DException) { restoreFailed = true; }
                try { if (bindingsMask & 8) gl.bindTexture(gl.TEXTURE_CUBE_MAP, previous.textureCube); }
                catch (restoreCubeException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_ALIGNMENT, previous.alignment); }
                catch (restoreAlignmentException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_ROW_LENGTH, previous.rowLength); }
                catch (restoreRowLengthException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_IMAGE_HEIGHT, previous.imageHeight); }
                catch (restoreImageHeightException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_SKIP_PIXELS, previous.skipPixels); }
                catch (restoreSkipPixelsException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_SKIP_ROWS, previous.skipRows); }
                catch (restoreSkipRowsException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_SKIP_IMAGES, previous.skipImages); }
                catch (restoreSkipImagesException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, previous.flipY); }
                catch (restoreFlipException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, previous.premultiply); }
                catch (restorePremultiplyException) { restoreFailed = true; }
                try { gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL, previous.colorspace); }
                catch (restoreColorspaceException) { restoreFailed = true; }
                if (restoreFailed) {
                    var restoreContextState = state.safeContextLost(gl);
                    if (restoreContextState > 0) {
                        state.markContextLost();
                        result = state.error.contextLost;
                    } else {
                        result = state.error.stateRestoreFailed;
                    }
                    if (writes !== 0 && failedRegion < 0)
                        failedRegion = regionCount - 1;
                }
            }
        }

        if (observeErrors || restoreFailed) {
            var restoreErrors = state.drainErrors(gl);
            if (state.errorObservationFailed(restoreErrors, gl)) {
                if (backendDetail === 0) backendDetail = restoreErrors.first;
                if (restoreErrors.contextLost) {
                    state.markContextLost();
                    result = state.error.contextLost;
                } else {
                    result = state.error.stateRestoreFailed;
                }
                if (writes !== 0 && failedRegion < 0) failedRegion = regionCount - 1;
            }
        }

        if (result === state.error.stateRestoreFailed)
            state.targets = {};

        state.freeSlot(slot);
        if (result === state.error.none) {
            state.addCounter('encoded', 1);
            state.addCounter('encodedPayloadBytes', payloadBytes);
            state.finishSequenceBatch(sequence, publicationTerminal, 0, -1, 0, writes);
            return state.terminal(batch, 0, -1, 0, 2, 0);
        }
        state.finishSequenceBatch(sequence, publicationTerminal, result,
            failedRegion, backendDetail, writes);
        return state.terminal(batch, result, failedRegion, backendDetail,
            result === state.error.contextLost ? 5 : 4, 0);
        } catch (outerException) {
            var outerContextState = state.safeContextLost(state.context);
            var outerError = outerContextState > 0 ? state.error.contextLost :
                accepted ? state.error.backendFailed : state.error.internal;
            if (outerContextState > 0) {
                try { state.markContextLost(); } catch (markException) {}
            }
            var outerFailedRegion = -1;
            if (accepted && typeof writes === 'number' && writes !== 0 &&
                typeof activeRegion === 'number')
                outerFailedRegion = Math.min(Math.max(activeRegion, 0),
                    typeof regionCount === 'number' ? regionCount - 1 : 0);
            if (accepted) {
                try { state.freeSlot(slot); } catch (retireException) {}
                state.finishSequenceBatch(sequence, publicationTerminal, outerError,
                    outerFailedRegion, 0, typeof writes === 'number' ? writes : 0);
                return state.terminal(batch, outerError, outerFailedRegion, 0,
                    outerError === state.error.contextLost ? 5 : 4, 0);
            }
            return state.reject(batch, outerError, -1, 0,
                outerError === state.error.contextLost ? 5 : 3, admissionStage);
        }
    },

    ls_gpu_v6_get_stats__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_get_stats: function(stats) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(stats, 120, 8)) return state.error.invalidArgument;
        state.clearBytes(stats, 120);
        try {
            if (state.epochExhausted) return state.error.internal;
            HEAPU32[stats >> 2] = 120;
            HEAPU32[(stats + 4) >> 2] = 0;
            HEAPU32[(stats + 8) >> 2] = state.epochLow;
            HEAPU32[(stats + 12) >> 2] = state.epochHigh;
            state.writeCounter(stats + 16, state.stats.accepted);
            state.writeCounter(stats + 24, state.stats.rejected);
            state.writeCounter(stats + 32, state.stats.encoded);
            HEAPU32[(stats + 40) >> 2] = 0;
            HEAPU32[(stats + 44) >> 2] = 0;
            state.writeCounter(stats + 48, state.stats.stale);
            state.writeCounter(stats + 56, state.stats.backpressure);
            state.writeCounter(stats + 64, state.stats.encodedPayloadBytes);
            var slotCount = 0;
            var slotsFree = 0;
            var slotsInFlight = 0;
            var slotCapacity = 0;
            var slotCapacityFree = 0;
            var slotCapacityInFlight = 0;
            for (var slotIndex = 0; slotIndex < state.slots.length; slotIndex++) {
                var slot = state.slots[slotIndex];
                slotCount++;
                slotCapacity += slot.capacity;
                if (slot.state === 0) {
                    slotsFree++;
                    slotCapacityFree += slot.capacity;
                } else {
                    slotsInFlight++;
                    slotCapacityInFlight += slot.capacity;
                }
            }
            state.writeValue64(stats + 72, slotCount);
            state.writeValue64(stats + 80, slotsFree);
            state.writeValue64(stats + 88, slotsInFlight);
            state.writeValue64(stats + 96, slotCapacity);
            state.writeValue64(stats + 104, slotCapacityFree);
            state.writeValue64(stats + 112, slotCapacityInFlight);
            return state.error.none;
        } catch (exception) {
            state.clearBytes(stats, 120);
            return state.exceptionError();
        }
    },

    ls_gpu_v6_abandon_session__deps: ['$LightSideGpuUploadV6'],
    ls_gpu_v6_abandon_session: function(epoch) {
        var state = LightSideGpuUploadV6;
        if (!state.fits(epoch, 8, 8)) return state.error.invalidArgument;
        HEAPU32[epoch >> 2] = 0;
        HEAPU32[(epoch + 4) >> 2] = 0;
        try {
            if (state.epochExhausted) return state.error.internal;
            if (state.epochLow === 0 && state.epochHigh === 0)
                return state.error.notInitialized;
            state.targets = {};
            state.sequences = {};
            state.sequenceHistory = [];
            state.sequenceCount = 0;
            state.trimFreeSlots();
            HEAPU32[epoch >> 2] = state.epochLow;
            HEAPU32[(epoch + 4) >> 2] = state.epochHigh;
            return state.error.none;
        } catch (exception) {
            HEAPU32[epoch >> 2] = 0;
            HEAPU32[(epoch + 4) >> 2] = 0;
            return state.exceptionError();
        }
    }
};

autoAddDeps(LightSideGpuUploadPlugin, '$LightSideGpuUploadV6');
mergeInto(LibraryManager.library, LightSideGpuUploadPlugin);
