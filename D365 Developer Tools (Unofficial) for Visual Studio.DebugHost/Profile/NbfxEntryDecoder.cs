using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;

namespace D365DeveloperTools.DebugHost.Profile
{
    /// <summary>Result of decoding one ProfileEntry's payload.</summary>
    internal sealed class DecodedEntry
    {
        public Guid Id { get; set; }

        /// <summary>The decoded root element's local name (e.g. "EntityReference", "ParameterCollection") — the type discriminator, since the envelope itself carries no separate type-code field (see ProfileEnvelopeReader's doc comment for why an earlier version wrongly assumed one existed).</summary>
        public string TypeName { get; set; }

        /// <summary>Set when TypeName matched a known SDK type — the strongly-typed decode via DataContractSerializer.</summary>
        public object Value { get; set; }

        /// <summary>Always set (when decoding succeeds at all) — the raw XML shape, for entries whose TypeName isn't in the catalog yet, and for diagnostics even when Value is set.</summary>
        public XElement RawXml { get; set; }

        public string FailureReason { get; set; }
        public bool Succeeded => FailureReason == null;
    }

    /// <summary>
    /// Decodes one entry's self-contained MC-NBFX payload via the BCL's own binary-XML reader
    /// (System.Runtime.Serialization.XmlDictionaryReader.CreateBinaryReader) — MC-NBFX is the public,
    /// documented Microsoft Open Specification for WCF's binary XML wire format, so this is the correct
    /// production path rather than a hand-rolled byte parser (one was only ever built, throwaway,
    /// during research to confirm the format at all).
    ///
    /// The type catalog is keyed by the decoded root element's name (not a numeric "type code" — the
    /// envelope has no such field; see ProfileEnvelopeReader) and is additive/known-incomplete on day
    /// one (only EntityReference and ParameterCollection confirmed against a live capture) — TryDecode
    /// always also returns the raw XML shape via XElement so an entry with an unrecognized type is still
    /// visible for diagnostics (and for building out the catalog further) rather than being silently dropped.
    /// </summary>
    internal static class NbfxEntryDecoder
    {
        private static readonly Dictionary<string, Type> KnownTypes = new Dictionary<string, Type>
        {
            ["EntityReference"] = typeof(EntityReference), // confirmed against a live environment — see the Plugin Debugging Phase 2 plan's research findings
            ["ParameterCollection"] = typeof(ParameterCollection), // confirmed against a live environment
            ["EntityImageCollection"] = typeof(EntityImageCollection), // confirmed against a live environment (bare/unwrapped entries)
        };

        public static DecodedEntry TryDecode(ProfileEntry entry)
        {
            var result = new DecodedEntry { Id = entry.Id };

            XElement rawXml;
            try
            {
                rawXml = ReadAsXElement(entry.Payload);
            }
            catch (Exception ex)
            {
                result.FailureReason = $"Entry {entry.Id}: payload did not parse as MC-NBFX — {ex.Message}";
                return result;
            }

            result.RawXml = rawXml;
            result.TypeName = rawXml.Name.LocalName;

            if (KnownTypes.TryGetValue(result.TypeName, out var knownType))
            {
                try
                {
                    result.Value = DeserializeKnownType(entry.Payload, knownType);
                }
                catch (Exception ex)
                {
                    // Non-fatal: the raw XML shape above still stands even if the strongly-typed
                    // DataContractSerializer decode failed (e.g. a shape mismatch worth investigating).
                    result.FailureReason = $"Entry {entry.Id} ({result.TypeName}): raw XML decoded but DataContractSerializer failed — {ex.Message}";
                }
            }

            return result;
        }

        private static XElement ReadAsXElement(byte[] payload)
        {
            using (var stream = new MemoryStream(payload))
            using (var reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                return XElement.Load(reader);
            }
        }

        private static object DeserializeKnownType(byte[] payload, Type type)
        {
            using (var stream = new MemoryStream(payload))
            using (var reader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
            {
                var serializer = new DataContractSerializer(type);
                return serializer.ReadObject(reader);
            }
        }
    }
}
