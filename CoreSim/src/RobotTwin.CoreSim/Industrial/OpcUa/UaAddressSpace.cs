using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    public enum UaNodeClass
    {
        Object = 1,
        Variable = 2,
        Method = 4,
        ObjectType = 8,
        VariableType = 16,
        ReferenceType = 32,
        DataType = 64,
        View = 128
    }

    public readonly struct UaReference
    {
        public UaReference(NodeId type, bool forward, NodeId target)
        {
            Type = type;
            IsForward = forward;
            Target = target;
        }

        public NodeId Type { get; }
        public bool IsForward { get; }
        public NodeId Target { get; }
    }

    /// <summary>[opcua] A node of the address space: its attributes and references; a variable's value comes from <see cref="Read"/>.</summary>
    public sealed class UaNode
    {
        public NodeId Id { get; set; }
        public UaNodeClass Class { get; set; }
        public QualifiedName BrowseName { get; set; } = new QualifiedName(0, string.Empty);
        public LocalizedText DisplayName { get; set; } = new LocalizedText(string.Empty);
        public LocalizedText? Description { get; set; }
        public List<UaReference> References { get; } = new List<UaReference>();
        public NodeId TypeDefinition { get; set; }

        /// <summary>Variables: their data type node (i=1 Boolean, i=11 Double …).</summary>
        public NodeId DataType { get; set; }

        public int ValueRank { get; set; } = -1;

        /// <summary>CurrentRead 1, CurrentWrite 2.</summary>
        public byte AccessLevel { get; set; } = 1;

        public double MinimumSamplingInterval { get; set; }
        public Func<DataValue>? Read { get; set; }

        /// <summary>Writes a value; returns its status (0 Good).</summary>
        public Func<Variant, uint>? Write { get; set; }

        public bool IsAbstract { get; set; }
        public bool Symmetric { get; set; }
        public LocalizedText? InverseName { get; set; }
    }

    /// <summary>
    /// [opcua] The nodes a server shows: the standard frame (Root, Objects, Types, Views, the Server object with its
    /// namespace array, server status and service level, the reference and data types a browser asks about), and the
    /// application's own objects and variables under Objects in namespace 1.
    /// </summary>
    public sealed class UaAddressSpace
    {
        private readonly Dictionary<NodeId, UaNode> _nodes = new Dictionary<NodeId, UaNode>();
        private readonly object _gate = new object();

        public const uint Organizes = 35, HasComponent = 47, HasProperty = 46, HasTypeDefinition = 40, HasSubtype = 45, HierarchicalReferences = 33,
            References = 31, HasChild = 34, Aggregates = 44, NonHierarchicalReferences = 32, HasEventSource = 36, HasNotifier = 48, HasModellingRule = 37, HasEncoding = 38;

        public const uint RootFolder = 84, ObjectsFolder = 85, TypesFolder = 86, ViewsFolder = 87, FolderType = 61, BaseObjectType = 58, BaseDataVariableType = 63, PropertyType = 68,
            Server = 2253, ServerArray = 2254, NamespaceArray = 2255, ServerStatus = 2256, StartTime = 2257, CurrentTime = 2258, State = 2259, ServiceLevel = 2267,
            ServerType = 2004, ServerStatusType = 2138;

        public DateTime Started { get; } = DateTime.UtcNow;

        public string ApplicationUri { get; }

        public UaAddressSpace(string applicationUri)
        {
            ApplicationUri = applicationUri;
            Frame();
        }

        public UaNode? Find(NodeId id)
        {
            lock (_gate) return _nodes.TryGetValue(id, out var n) ? n : null;
        }

        public IReadOnlyList<UaNode> Nodes
        {
            get
            {
                lock (_gate) return _nodes.Values.ToList();
            }
        }

        public UaNode Add(UaNode node, NodeId parent, uint referenceType)
        {
            lock (_gate)
            {
                _nodes[node.Id] = node;
                if (_nodes.TryGetValue(parent, out var p)) p.References.Add(new UaReference(NodeId.Ns0(referenceType), true, node.Id));
                node.References.Add(new UaReference(NodeId.Ns0(referenceType), false, parent));
                if (!node.TypeDefinition.IsNull) node.References.Add(new UaReference(NodeId.Ns0(HasTypeDefinition), true, node.TypeDefinition));
            }

            return node;
        }

        /// <summary>Removes a node and the references to it (an application object leaving).</summary>
        public void Remove(NodeId id)
        {
            lock (_gate)
            {
                if (!_nodes.Remove(id)) return;
                foreach (var n in _nodes.Values) n.References.RemoveAll(r => r.Target == id);
            }
        }

        public UaNode AddObject(NodeId id, string name, NodeId parent, uint referenceType = Organizes, uint typeDefinition = BaseObjectType) =>
            Add(new UaNode
            {
                Id = id,
                Class = UaNodeClass.Object,
                BrowseName = new QualifiedName(id.Namespace, name),
                DisplayName = new LocalizedText(name),
                TypeDefinition = NodeId.Ns0(typeDefinition)
            }, parent, referenceType);

        public UaNode AddVariable(NodeId id, string name, NodeId parent, uint dataType, Func<DataValue> read, Func<Variant, uint>? write = null,
            uint referenceType = HasComponent, uint typeDefinition = BaseDataVariableType) =>
            Add(new UaNode
            {
                Id = id,
                Class = UaNodeClass.Variable,
                BrowseName = new QualifiedName(id.Namespace, name),
                DisplayName = new LocalizedText(name),
                TypeDefinition = NodeId.Ns0(typeDefinition),
                DataType = NodeId.Ns0(dataType),
                Read = read,
                Write = write,
                AccessLevel = (byte)(write != null ? 3 : 1)
            }, parent, referenceType);

        /// <summary>Is <paramref name="type"/> the reference type asked for, or (with subtypes) one of its subtypes?</summary>
        public static bool Matches(NodeId type, NodeId asked, bool subtypes)
        {
            if (asked.IsNull || type == asked) return true;
            if (!subtypes || asked.Namespace != 0 || type.Namespace != 0) return false;
            uint t = type.Numeric;
            switch (asked.Numeric)
            {
                case References: return true;
                case HierarchicalReferences: return t is HasChild or Organizes or HasEventSource or HasComponent or HasProperty or HasSubtype or Aggregates or HasNotifier;
                case HasChild: return t is Aggregates or HasComponent or HasProperty or HasSubtype;
                case Aggregates: return t is HasComponent or HasProperty;
                case NonHierarchicalReferences: return t is HasTypeDefinition or HasModellingRule or HasEncoding;
                case HasEventSource: return t == HasNotifier;
                default: return false;
            }
        }

        private static byte[] Encode(Action<UaWriter> write)
        {
            var w = new UaWriter();
            write(w);
            return w.ToArray();
        }

        private void Frame()
        {
            void Raw(uint id, UaNodeClass cls, string name)
            {
                _nodes[NodeId.Ns0(id)] = new UaNode { Id = NodeId.Ns0(id), Class = cls, BrowseName = new QualifiedName(0, name), DisplayName = new LocalizedText(name) };
            }

            Raw(RootFolder, UaNodeClass.Object, "Root");
            _nodes[NodeId.Ns0(RootFolder)].TypeDefinition = NodeId.Ns0(FolderType);
            _nodes[NodeId.Ns0(RootFolder)].References.Add(new UaReference(NodeId.Ns0(HasTypeDefinition), true, NodeId.Ns0(FolderType)));
            AddObject(NodeId.Ns0(ObjectsFolder), "Objects", NodeId.Ns0(RootFolder), Organizes, FolderType);
            AddObject(NodeId.Ns0(TypesFolder), "Types", NodeId.Ns0(RootFolder), Organizes, FolderType);
            AddObject(NodeId.Ns0(ViewsFolder), "Views", NodeId.Ns0(RootFolder), Organizes, FolderType);

            // The types a browser reads the names of.
            foreach (var (id, name) in new (uint, string)[]
                     {
                         (References, "References"), (HierarchicalReferences, "HierarchicalReferences"), (NonHierarchicalReferences, "NonHierarchicalReferences"),
                         (HasChild, "HasChild"), (Organizes, "Organizes"), (HasEventSource, "HasEventSource"), (HasModellingRule, "HasModellingRule"),
                         (HasEncoding, "HasEncoding"), (HasTypeDefinition, "HasTypeDefinition"), (Aggregates, "Aggregates"), (HasSubtype, "HasSubtype"),
                         (HasProperty, "HasProperty"), (HasComponent, "HasComponent"), (HasNotifier, "HasNotifier")
                     })
            {
                Raw(id, UaNodeClass.ReferenceType, name);
                _nodes[NodeId.Ns0(id)].IsAbstract = id is References or HierarchicalReferences or NonHierarchicalReferences or HasChild or Aggregates;
            }

            foreach (var (id, name) in new (uint, string)[]
                     {
                         (1, "Boolean"), (2, "SByte"), (3, "Byte"), (4, "Int16"), (5, "UInt16"), (6, "Int32"), (7, "UInt32"), (8, "Int64"), (9, "UInt64"),
                         (10, "Float"), (11, "Double"), (12, "String"), (13, "DateTime"), (21, "LocalizedText"), (24, "BaseDataType"), (852, "ServerState"), (862, "ServerStatusDataType")
                     })
                Raw(id, UaNodeClass.DataType, name);
            foreach (var (id, name) in new (uint, string)[] { (BaseObjectType, "BaseObjectType"), (FolderType, "FolderType"), (ServerType, "ServerType") })
                Raw(id, UaNodeClass.ObjectType, name);
            foreach (var (id, name) in new (uint, string)[] { (BaseDataVariableType, "BaseDataVariableType"), (PropertyType, "PropertyType"), (ServerStatusType, "ServerStatusType") })
                Raw(id, UaNodeClass.VariableType, name);

            var server = AddObject(NodeId.Ns0(Server), "Server", NodeId.Ns0(ObjectsFolder), Organizes, ServerType);
            DataValue Now(Variant v) => new DataValue { Value = v, SourceTimestamp = DateTime.UtcNow, ServerTimestamp = DateTime.UtcNow };
            AddVariable(NodeId.Ns0(NamespaceArray), "NamespaceArray", server.Id, 12,
                () => Now(new Variant(UaType.String, new object?[] { "http://opcfoundation.org/UA/", ApplicationUri })), null, HasProperty, PropertyType).ValueRank = 1;
            AddVariable(NodeId.Ns0(ServerArray), "ServerArray", server.Id, 12,
                () => Now(new Variant(UaType.String, new object?[] { ApplicationUri })), null, HasProperty, PropertyType).ValueRank = 1;
            AddVariable(NodeId.Ns0(ServiceLevel), "ServiceLevel", server.Id, 3, () => Now(new Variant(UaType.Byte, (byte)255)), null, HasProperty, PropertyType);
            // ServerStatusDataType (binary encoding i=864): start and current time, state, build info, shutdown.
            var status = AddVariable(NodeId.Ns0(ServerStatus), "ServerStatus", server.Id, 862, () => Now(new Variant(UaType.ExtensionObject, new ExtensionObject
            {
                TypeId = NodeId.Ns0(864),
                Body = Encode(w =>
                {
                    w.DateTime(Started);
                    w.DateTime(DateTime.UtcNow);
                    w.Int32(0);   // Running
                    w.String("urn:robotwin:studio");
                    w.String("RobotWin");
                    w.String("RobotWin Studio");
                    w.String(typeof(UaAddressSpace).Assembly.GetName().Version?.ToString() ?? "1.0");
                    w.String("1");
                    w.DateTime(Started);
                    w.UInt32(0);
                    w.LocalizedText(null);
                })
            })), null, HasComponent, ServerStatusType);
            AddVariable(NodeId.Ns0(StartTime), "StartTime", status.Id, 13, () => Now(new Variant(UaType.DateTime, Started)));
            AddVariable(NodeId.Ns0(CurrentTime), "CurrentTime", status.Id, 13, () => Now(new Variant(UaType.DateTime, DateTime.UtcNow)));
            AddVariable(NodeId.Ns0(State), "State", status.Id, 852, () => Now(new Variant(UaType.Int32, 0)));   // Running
        }
    }
}
