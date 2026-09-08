using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace D365DeveloperTools.DebugHost.Profile
{
    /// <summary>One entry from a decoded plugintracelog.profile envelope — a self-contained MC-NBFX document, not yet parsed.</summary>
    internal sealed class ProfileEntry
    {
        public Guid Id { get; set; }

        /// <summary>Diagnostic only — not used to determine the decoded type (see NbfxEntryDecoder, which uses the payload's own decoded root element name instead). -1 for bare entries, which have no type code at all.</summary>
        public int TypeCode { get; set; }
        public byte[] Payload { get; set; }

        /// <summary>Byte offset in the raw profile blob where this entry started — kept for diagnostics only (e.g. reporting where a resync happened).</summary>
        public int EntryStart { get; set; }

        /// <summary>
        /// True when this entry had no GUID/typecode wrapper at all — just a length-prefixed payload,
        /// recovered by letting the binary XML reader self-terminate a bare Element/Text record directly
        /// (see TryReadBareEntry). Id is Guid.Empty and TypeCode is -1 for these; they're real decodable
        /// data (confirmed: full Entity/EntityImageCollection object graphs), just not tagged the way the
        /// confirmed EntityReference/ParameterCollection entries are.
        /// </summary>
        public bool IsBare { get; set; }
    }

    /// <summary>
    /// Splits the raw bytes decoded from plugintracelog.profile into independently-serialized entries.
    /// This is pure byte-framing — no XML/NBFX awareness at all (except TryReadBareEntry's use of the
    /// binary reader purely as a self-terminating scanner, not for interpretation) — deliberately
    /// isolated from NbfxEntryDecoder, which parses one entry's own payload once extracted.
    ///
    /// Two real bugs were found and fixed empirically against live-captured samples before this shape
    /// was reached:
    /// 1. The type-code field is a single raw byte, not a MultiByteInt31 varint. An earlier version read
    ///    it as MultiByteInt31, which happened to work for the one entry whose real type-code byte was
    ///    &lt; 0x80 (no continuation bit) but silently corrupted every entry whose real byte was &gt;= 0x80 —
    ///    true of most real type codes — by merging it with the following byte.
    /// 2. Not every entry is GUID/typecode-tagged at all. The byte immediately after the tag is a genuine
    ///    multi-byte varint; 0x24 (36) is just the value it happens to hold when a GUID follows (ASCII
    ///    GUIDs are always exactly 36 characters) — so when that varint is something else (or 36 bytes
    ///    that don't actually parse as a GUID), it isn't a mis-synced tagged entry, it's a *different,
    ///    simpler* frame this format also uses: [tag][varint payload length][payload], no id, no type
    ///    code at all (confirmed: this is exactly how a full EntityImageCollection document — real
    ///    Attributes/Id/LogicalName/RowVersion data — sits in the file with no wrapper). TryReadBareEntry
    ///    handles this by letting the binary reader consume exactly one self-terminating document via
    ///    ReadSubtree(), rather than needing to know its length up front.
    /// </summary>
    internal static class ProfileEnvelopeReader
    {
        private const int GuidAsciiLength = 36;

        private const byte MinElementRecordType = 0x40;
        private const byte MaxElementRecordType = 0x77;
        private const byte MinTextRecordType = 0x80;
        private const byte MaxTextRecordType = 0xBD;

        public static IReadOnlyList<ProfileEntry> SplitEntries(byte[] rawProfileBytes, out List<string> resyncNotes)
        {
            resyncNotes = new List<string>();
            var entries = new List<ProfileEntry>();
            if (rawProfileBytes == null || rawProfileBytes.Length < 4) { return entries; }

            // A small fixed header precedes the first entry — confirmed 2 bytes against real samples
            // (0x08 0x01); its purpose isn't confirmed, so it's skipped rather than interpreted.
            var pos = 2;

            while (pos < rawProfileBytes.Length)
            {
                var entryStart = pos;
                if (!TryReadEntry(rawProfileBytes, ref pos, entryStart, out var entry))
                {
                    // Fall back to a bare, unwrapped document at the same position before giving up and
                    // resyncing — see the class-level comment, point 2.
                    pos = entryStart;
                    if (TryReadBareEntry(rawProfileBytes, ref pos, entryStart, out entry))
                    {
                        entries.Add(entry);
                        continue;
                    }

                    var resyncPos = FindNextEntryStart(rawProfileBytes, entryStart + 1, out var resyncIsBare);
                    if (resyncPos >= 0 && resyncIsBare)
                    {
                        var bareResyncPos = resyncPos;
                        if (TryReadBareEntry(rawProfileBytes, ref bareResyncPos, resyncPos, out var bareResyncEntry))
                        {
                            resyncNotes.Add($"Could not parse an entry at offset {entryStart} — resynced by scanning forward to offset {resyncPos} (a bare entry, not GUID-tagged — otherwise unreachable by GUID-pattern scanning alone).");
                            entries.Add(bareResyncEntry);
                            pos = bareResyncPos;
                            continue;
                        }
                    }
                    if (resyncPos < 0)
                    {
                        resyncNotes.Add($"Could not parse an entry at offset {entryStart}, and no further plausible entry start was found in the remaining {rawProfileBytes.Length - entryStart} bytes — stopping.");
                        break;
                    }

                    resyncNotes.Add($"Could not parse an entry at offset {entryStart} — resynced by scanning forward to offset {resyncPos}.");
                    pos = resyncPos;
                    continue;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private static bool TryReadEntry(byte[] bytes, ref int pos, int entryStart, out ProfileEntry entry)
        {
            entry = null;
            var p = pos;

            if (p >= bytes.Length) { return false; }
            p++; // tag byte — not currently validated against a fixed value; see FindNextEntryStart for the check that anchors resync instead.

            if (!TryReadMultiByteInt31(bytes, ref p, out var lengthOrGuidMarker)) { return false; }

            if (lengthOrGuidMarker == GuidAsciiLength && p + GuidAsciiLength <= bytes.Length &&
                Guid.TryParse(Encoding.ASCII.GetString(bytes, p, GuidAsciiLength), out var id))
            {
                p += GuidAsciiLength;

                // Single raw byte, NOT MultiByteInt31 — see class-level comment, point 1.
                if (p >= bytes.Length) { return false; }
                var typeCode = bytes[p];
                p++;

                if (!TryReadMultiByteInt31(bytes, ref p, out var payloadLength)) { return false; }
                if (!TryTakePayload(bytes, ref p, payloadLength, out var taggedPayload)) { return false; }

                entry = new ProfileEntry { Id = id, TypeCode = typeCode, Payload = taggedPayload, EntryStart = entryStart };
                pos = p;
                return true;
            }

            // Not GUID-tagged: lengthOrGuidMarker is the payload's own byte length directly.
            if (!TryTakePayload(bytes, ref p, lengthOrGuidMarker, out var barePayload)) { return false; }

            entry = new ProfileEntry { Id = Guid.Empty, TypeCode = -1, Payload = barePayload, EntryStart = entryStart, IsBare = true };
            pos = p;
            return true;
        }

        /// <summary>Validates and copies out a payload of the given declared length, requiring its first byte to look like a real MC-NBFX record start.</summary>
        private static bool TryTakePayload(byte[] bytes, ref int p, int payloadLength, out byte[] payload)
        {
            payload = null;
            if (payloadLength <= 0 || p + payloadLength > bytes.Length) { return false; }

            var firstPayloadByte = bytes[p];
            var looksLikeElement = firstPayloadByte >= MinElementRecordType && firstPayloadByte <= MaxElementRecordType;
            var looksLikeBareText = firstPayloadByte >= MinTextRecordType && firstPayloadByte <= MaxTextRecordType;
            if (!looksLikeElement && !looksLikeBareText) { return false; }

            payload = new byte[payloadLength];
            Buffer.BlockCopy(bytes, p, payload, 0, payloadLength);
            p += payloadLength;
            return true;
        }

        /// <summary>
        /// Attempts to read one plain, unwrapped, self-terminating MC-NBFX document starting exactly at
        /// <paramref name="pos"/> — no GUID/typecode/length header at all, just a raw Element or Text
        /// record. Confirmed against real captures: the byte range immediately after a confirmed
        /// EntityReference entry is exactly one such document (an EntityImageCollection full of real
        /// Attributes/Id/LogicalName data), not a resync gap.
        ///
        /// The key trick is ReadSubtree(): XElement.Load/ReadOuterXml both do one extra Read() past the
        /// closing tag to confirm nothing else follows, and the binary reader treats any later sibling
        /// content still in the underlying stream as a second document root and throws — even though we
        /// only ever asked for the one element. ReadSubtree() scopes a sub-reader to just this element's
        /// own span, so reading it to completion never looks past its EndElement, and needs no
        /// foreknowledge of where the real boundary is.
        /// </summary>
        private static bool TryReadBareEntry(byte[] bytes, ref int pos, int entryStart, out ProfileEntry entry)
        {
            entry = null;
            if (pos >= bytes.Length) { return false; }

            var firstByte = bytes[pos];
            var looksLikeElement = firstByte >= MinElementRecordType && firstByte <= MaxElementRecordType;
            var looksLikeBareText = firstByte >= MinTextRecordType && firstByte <= MaxTextRecordType;
            if (!looksLikeElement && !looksLikeBareText) { return false; }

            using (var stream = new MemoryStream(bytes, pos, bytes.Length - pos, writable: false))
            using (var reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                try
                {
                    if (looksLikeElement)
                    {
                        reader.MoveToContent();
                        using (var subReader = reader.ReadSubtree())
                        {
                            subReader.MoveToContent();
                            while (subReader.Read()) { } // drain to the matching EndElement without a further lookahead past it
                        }
                    }
                    else
                    {
                        reader.MoveToContent();
                        reader.ReadContentAsString();
                    }
                }
                catch
                {
                    return false;
                }

                var consumed = (int)stream.Position;
                if (consumed <= 0) { return false; }

                var payload = new byte[consumed];
                Buffer.BlockCopy(bytes, pos, payload, 0, consumed);
                entry = new ProfileEntry { Id = Guid.Empty, TypeCode = -1, Payload = payload, EntryStart = entryStart, IsBare = true };
                pos += consumed;
                return true;
            }
        }

        /// <summary>
        /// Scans forward for the next position where a full entry actually validates — either a
        /// GUID-tagged entry (checked first, since a GUID-shaped byte run is a strong, specific anchor)
        /// or a bare/untagged one (checked at every position whose first byte merely looks plausible —
        /// necessary, not optional: a bare entry has no GUID to anchor on at all, so without this check
        /// a bare entry sitting right after a parse failure could never be recovered — confirmed against
        /// a live capture, where a whole second ParameterCollection [containing the plugin's actual
        /// Target parameter] was silently skipped for exactly this reason before this check existed).
        /// A plain GUID-shaped byte run alone was too weak a signal early on (real captured entity data
        /// is full of embedded GUIDs — record ids, user ids, correlation ids — so it kept matching
        /// unrelated data), which is why both paths require the FULL entry to actually validate, not
        /// just its leading shape, before this returns a position — that also keeps this from
        /// degenerating into quadratic rescanning over a ~16KB file.
        /// </summary>
        private static int FindNextEntryStart(byte[] bytes, int fromPos, out bool isBare)
        {
            isBare = false;

            for (var p = fromPos; p < bytes.Length; p++)
            {
                // Bare-entry check: cheap pre-filter (first byte in a plausible range) before the more
                // expensive attempt, exactly as TryReadBareEntry itself does when called directly.
                var firstByte = bytes[p];
                var looksLikeElement = firstByte >= MinElementRecordType && firstByte <= MaxElementRecordType;
                var looksLikeBareText = firstByte >= MinTextRecordType && firstByte <= MaxTextRecordType;
                if (looksLikeElement || looksLikeBareText)
                {
                    var bareTestPos = p;
                    if (TryReadBareEntry(bytes, ref bareTestPos, p, out _))
                    {
                        isBare = true;
                        return p;
                    }
                }

                // GUID-tagged check.
                if (p + GuidAsciiLength <= bytes.Length)
                {
                    var guidText = Encoding.ASCII.GetString(bytes, p, GuidAsciiLength);
                    if (Guid.TryParse(guidText, out _))
                    {
                        var candidateEntryStart = p - 2; // back up over the 1-byte tag + 1-byte MB31 length-marker (36 < 128, always 1 byte)
                        if (candidateEntryStart >= 0)
                        {
                            var testPos = candidateEntryStart;
                            if (TryReadEntry(bytes, ref testPos, candidateEntryStart, out _))
                            {
                                return candidateEntryStart;
                            }
                        }
                    }
                }
            }

            return -1;
        }

        private static bool TryReadMultiByteInt31(byte[] bytes, ref int pos, out int value)
        {
            value = 0;
            var shift = 0;

            for (var i = 0; i < 5; i++)
            {
                if (pos >= bytes.Length) { return false; }
                var b = bytes[pos];
                pos++;
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) { return true; }
                shift += 7;
            }

            return false; // malformed — more than 5 continuation bytes
        }
    }
}
