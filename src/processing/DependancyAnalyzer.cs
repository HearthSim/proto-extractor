using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using protoextractor.IR;

namespace protoextractor.processing
{
    class DependancyAnalyzer
    {
        /*
         * This class attempts to resolve circular dependancies in the given program structure. 
         */

        enum NODE_STATE
        {
            ALIVE,
            DEAD,
            UNDEAD
        }

        // Object containing our IR structure.
        private IRProgram _program;

        // Maps types to their parent namespace.
        Dictionary<IRTypeNode, IRNamespace> _TypeNSMapper;

        // Dependancies between namespaces.
        Dictionary<IRNamespace, HashSet<IRNamespace>> _NSDependancies;

        // Direct relations between types.
        Dictionary<IRTypeNode, HashSet<IRTypeNode>> _TypeDependancies;

        public DependancyAnalyzer(IR.IRProgram program)
        {
            _program = program;

            _TypeNSMapper = new Dictionary<IRTypeNode, IRNamespace>();
            _NSDependancies = new Dictionary<IRNamespace, HashSet<IRNamespace>>();
            _TypeDependancies = new Dictionary<IRTypeNode, HashSet<IRTypeNode>>();
        }

        // Processes the given program (INPLACE) and returns it.
        public IRProgram Process()
        {
            // Loop until no more circular dependancies are known.
            while (true)
            {
                // Construct dependancy data.
                CreateDependancyGraph();

                try
                {
                    // Find and Resolve TYPE circular dependancies.
                    CreateTopologicalTypeTree();
                }
                catch (CircularException<IRTypeNode> e)
                {
                    // Resolve the circular type thing.
                    var circle = e.CircularDependancies;
                    ResolveCircularTypes(circle);

                    // -> Continue with the loop..
                    continue;
                }


                try
                {
                    // Find and Resolve NAMESPACE circular dependancies..
                    CreateTopologicalNSTree();
                }
                catch (CircularException<IRNamespace> e)
                {
                    var circle = e.CircularDependancies;
                    // Resolve the circular namespaces.
                    ResolveCircularNS(circle);

                    // -> Continue with the loop..
                    continue;
                }

                break;
            }

            return _program;
        }

        // Generate and store dependancies between namespaces AND types.
        private void CreateDependancyGraph()
        {
            // Clear all known data.
            _TypeNSMapper.Clear();
            _NSDependancies.Clear();
            _TypeDependancies.Clear();

            var orderedNamespaces = _program.Namespaces.OrderBy(ns => ns.FullName);

            foreach (var ns in orderedNamespaces)
            {
                // Set for all namespaces referenced by this namespace.
                var nsDepSet = new HashSet<IRNamespace>();
                _NSDependancies[ns] = nsDepSet;

                // For each reference property, find the parent namespace and store that one.
                foreach (var irClass in ns.Classes)
                {
                    // Inverse mapping between namespace and type.
                    _TypeNSMapper[irClass] = ns;

                    // Set for all types referenced by this specific type.
                    var typeDepSet = new HashSet<IRTypeNode>();
                    _TypeDependancies[irClass] = typeDepSet;

                    foreach (var property in irClass.Properties)
                    {
                        if (property.Type == PropertyTypeKind.TYPE_REF)
                        {
                            // The reference IR type.
                            var refType = property.ReferencedType;
                            // The parent namespace of the referenced type.
                            // This must be resolved recursively! Future requests for the namespace
                            // of a type should be done through '_TypeNSMapper'.
                            var refParent = refType.Parent;
                            while (!(refParent is IRNamespace))
                            {
                                refParent = refParent.Parent;
                            }
                            var refParentNS = refParent as IRNamespace;

                            // Store the dependancy between current namespace and the referenced one.
                            nsDepSet.Add(refParentNS);
                            // Store the reference between current type and the referenced one.
                            typeDepSet.Add(refType);
                        }
                    }
                }

                // Loop each enum for inverse namespace mapping.
                // Enums do NOT have references to other types!
                foreach (var irEnum in ns.Enums)
                {
                    _TypeNSMapper[irEnum] = ns;
                    _TypeDependancies[irEnum] = new HashSet<IRTypeNode>();
                }

                // Remove our own namespace as dependancy.
                nsDepSet.Remove(ns);
            }
        }

        #region TYPE_RESOLVE
        // Creates a topological tree from the dependancies of the specified type.
        private List<IRTypeNode> CreateTopologicalTypeTree()
        {
            // List of all visited types.
            List<IRTypeNode> visited = new List<IRTypeNode>();
            // Topological view of the dependant types.
            List<IRTypeNode> topologicalOrder = new List<IRTypeNode>();

            // Meta state of each type.
            Dictionary<IRTypeNode, NODE_STATE> nodeState = new Dictionary<IRTypeNode, NODE_STATE>();


            // Collection of all IR Types.
            var allTypes = _TypeDependancies.Keys.ToList();

            // Mark all nodes ALIVE, this means they still need to be processed.
            // The main resource of this algorithm is the '_TypeDependancies' variable.
            foreach (var type in allTypes)
            {
                nodeState[type] = NODE_STATE.ALIVE;
            }


            foreach (var node in allTypes)
            {
                // Start the topological tree from the given node.
                // TopologicalVisit calls itself recursively.
                TypeTopologicalVisit(node, topologicalOrder, nodeState, visited);
            }

            return topologicalOrder;
        }

        private void TypeTopologicalVisit(IRTypeNode node, List<IRTypeNode> topologicalOrder,
            Dictionary<IRTypeNode, NODE_STATE> nodeState, List<IRTypeNode> visitedNodes)
        {
            // Each type is represented as a NODE.
            // NODE STATES:
            //  - ALIVE: Node needs to be processed.
            //  - UNDEAD: Currently processing this node.
            //  - DEAD: This node has been succesfully visited.

            IRTypeNode targetNode = node;

            // If the node is a private type, we take it's parent that's public, recursively.
            while (targetNode.IsPrivate == true)
            {
                if (targetNode.Parent is IRNamespace)
                {
                    throw new Exception("Type cannot be private child of namespace!");
                }

                targetNode = targetNode.Parent as IRTypeNode;
            }

            NODE_STATE state = nodeState[targetNode];
            if (state == NODE_STATE.DEAD)
            {
                // This node has been processed, return to avoid running forever.
                return;
            }

            // Visit this node.
            visitedNodes.Add(targetNode);

            // An undead state means we hit a circular dependancy!
            if (state == NODE_STATE.UNDEAD)
            {
                // References from Parent node to child (nested) node are allowed.
                // Also self references!
                // eg; PARENT -> CHILD (=PARENT) ..
                // The last item is always 'targetNode'. If the last but one item is also 'targetNode'
                // we know it's one of the above allowed operations.
                var oneButLastIdx = visitedNodes.Count() - 2;
                if (visitedNodes[oneButLastIdx] == targetNode)
                {
                    return;
                }

                // Create a list of all nodes within our circle.
                var circleEntryIDx = visitedNodes.IndexOf(targetNode);
                // Do not switch into 0-based indexing, because we want a list beginning and ending
                // with the same node!
                var circleLength = visitedNodes.Count() - circleEntryIDx;
                var circle = visitedNodes.GetRange(circleEntryIDx, circleLength);

                // If each type belongs to the SAME namespace, there is no circular dependancy!
                if (AllTypesInSameNamespace(circle))
                {
                    return;
                }

                throw new CircularException<IRTypeNode>(circle);
            }

            // Node is ALIVE and will be processed.
            nodeState[targetNode] = NODE_STATE.UNDEAD;
            var dependancies = _TypeDependancies[targetNode];
            // Visit all dependancies of this node.
            foreach (var dep in dependancies)
            {
                // Send in a copy of visited nodes so it doesn't litter the list
                // with non circular types.
                TypeTopologicalVisit(dep, topologicalOrder, nodeState, visitedNodes.ToList());
            }
            // Node is recursiely visited.
            nodeState[targetNode] = NODE_STATE.DEAD;
            topologicalOrder.Add(targetNode);
        }

        private void ResolveCircularTypes(List<IRTypeNode> circle)
        {
            // All unique types within the given list are moved together into a seperate
            // namespace, because that's the only way to solve these kind of circular dependancies.
            // This list will NEVER contain enums!
            var distincttypes = circle.Distinct().Cast<IRClass>();

            // TODO: Better naming algorithm?! (Longest commong substring)
            // The longest substring between the fullnames of each type's namespace.
            // TODO: This might clash with existing namespace names! check for that..
            var allTypeNamespaceNames = circle.Select(type => _TypeNSMapper[type].FullName).ToList();
            var newNSName = DependancyUtil.LongestmatchingSubstring(allTypeNamespaceNames).Trim('.');
            // Also construct a shortname from the new namespace fullname
            var newNSShortName = newNSName.Substring(newNSName.LastIndexOf('.') + 1);

            var nsEndPart = ".extracted";
            newNSName = newNSName + nsEndPart;
            newNSShortName = newNSShortName + nsEndPart;

            var newNS = new IRNamespace(newNSName, newNSShortName)
            {
                Classes = new List<IRClass>(),
                Enums = new List<IREnum>(),
            };

            foreach (var irClass in distincttypes)
            {
                var oldNS = _TypeNSMapper[irClass];
                MoveType(irClass, oldNS, newNS);
            }

            // Add new namespace to the program.
            _program.Namespaces.Add(newNS);
        }

        #endregion

        #region NS_RESOLVE

        private List<IRNamespace> CreateTopologicalNSTree()
        {
            // Create a tree with smaller namespace fullnames closer to the root.
            // And more imports near the leaves..

            // Also do a topological visit on all namespaces.
            List<IRNamespace> visited = new List<IRNamespace>();
            List<IRNamespace> topologicalOrder = new List<IRNamespace>();

            Dictionary<IRNamespace, NODE_STATE> nodeState = new Dictionary<IRNamespace, NODE_STATE>();

            // All known namespaces, ordered by ascending name.
            var allNamespaces = _program.Namespaces.OrderBy(ns => ns.FullName);

            // Mark all namespaces as unvisited.
            foreach (var ns in allNamespaces)
            {
                nodeState[ns] = NODE_STATE.ALIVE;
            }

            // Topological walk the dependancy graph.
            foreach (var ns in allNamespaces)
            {
                NSTopologicalVisit(ns, topologicalOrder, nodeState, visited);
            }


            return topologicalOrder;
        }

        // Function almost identical to TypeTopologicalVisit(..)
        private void NSTopologicalVisit(IRNamespace node, List<IRNamespace> topologicalOrder,
            Dictionary<IRNamespace, NODE_STATE> nodeState, List<IRNamespace> visitedNodes)
        {
            // Each type is represented as a NODE.
            // NODE STATES:
            //  - ALIVE: Node needs to be processed.
            //  - UNDEAD: Currently processing this node.
            //  - DEAD: This node has been succesfully visited.

            NODE_STATE state = nodeState[node];
            if (state == NODE_STATE.DEAD)
            {
                return;
            }

            visitedNodes.Add(node);

            if (state == NODE_STATE.UNDEAD)
            {
                // Create a list of all nodes within our circle.
                var circleEntryIDx = visitedNodes.IndexOf(node);
                // Do not switch into 0-based indexing, because we want a list beginning and ending
                // with the same node!
                var circleLength = visitedNodes.Count() - circleEntryIDx;
                var circle = visitedNodes.GetRange(circleEntryIDx, circleLength);

                throw new CircularException<IRNamespace>(circle);
            }

            // Node is ALIVE and will be processed.
            nodeState[node] = NODE_STATE.UNDEAD;
            // Find all dependancies of the current namespace.
            var dependancies = _NSDependancies[node];
            // Visit each namespace dependancy.
            foreach (var dep in dependancies)
            {
                // Send a copy of visited nodes to not litter the list of circular dependant nodes
                // when we reach an undead node.
                NSTopologicalVisit(dep, topologicalOrder, nodeState, visitedNodes.ToList());
            }

            // Node is recursively visited.
            nodeState[node] = NODE_STATE.DEAD;
            topologicalOrder.Add(node);
        }

        // The list of types start and end with the same type!
        private void ResolveCircularNS(List<IRNamespace> circle)
        {
            if (circle.Count() > 3)
            {
                throw new Exception("This method is not created for solving big circular dependancies!");
            }

            // Suppose there are ONLY DIRECT CIRCULAR DEPENDANCIES.
            // This means one type points to another which points to the first!
            var nsOne = circle[0];
            var nsTwo = circle[1];

            // Decide which namespace is not allowed to point to the other.
            var cmpNS = CompareNSHeights(nsOne, nsTwo);
            // Namespaces indicating where types will be moved towards.
            // By default, nsOne is the target; nsTwo the source.
            IRNamespace source = nsTwo;
            IRNamespace target = nsOne;
            if (cmpNS == 1)
            {
                // The compare function says otherwise, so swap source/target.
                source = nsOne;
                target = nsTwo;
            }

            // Select all types that need to be moved from source.
            var typesToMove = GetAllReferencingClasses(source, target);
            // Move these types to target.
            foreach (var type in typesToMove)
            {
                MoveType(type, source, target);
            }

        }

        // Returns a list of classes that have a property referencing another type in the target namespace.
        // All classes from 'source' will be inspected. All referenced types will be checked to have a parent
        // namespace equal to 'referenced'.
        private List<IRClass> GetAllReferencingClasses(IRNamespace source, IRNamespace referenced)
        {
            List<IRClass> returnValue = new List<IRClass>();
            foreach (var ilClass in source.Classes)
            {
                // Loop each dependancy for the selected type.
                var deps = _TypeDependancies[ilClass];
                foreach (var dep in deps)
                {
                    // Find and test the parent namespace of the reference.
                    var parentNS = _TypeNSMapper[dep];
                    if (parentNS == referenced)
                    {
                        // The namespace matches the references one, so we track the 
                        // referencing class.
                        returnValue.Add(ilClass);
                    }
                }
            }
            return returnValue;
        }

        // Compares the heights of both namespaces in a mental NON-CIRCULAR TREE.
        // The tree has the limitation that references are only allowed towards the root.
        // This method returns -1 if nsOne is closer to the root.
        // This method returns +1 is nsTwo is closer to the root.
        private int CompareNSHeights(IRNamespace nsOne, IRNamespace nsTwo)
        {
            List<string> nsNames = new List<string>();
            nsNames.Add(nsOne.FullName);
            nsNames.Add(nsTwo.FullName);
            var commonSubstr = DependancyUtil.LongestmatchingSubstring(nsNames);
            // Both namespaces share a hypothetical parent namespace.
            if (commonSubstr.Count() > 0)
            {
                return nsOne.FullName.CompareTo(nsTwo.FullName);
            }

            // If there is no shared namespace, resort to amount of imports both types do.
            var depsNSOne = _NSDependancies[nsOne].Count();
            var depsNSTwo = _NSDependancies[nsTwo].Count();
            // NSTwo has less dependancies, so it's considered closer to the root.
            // This is a vague heuristic, ideally the one closer to the root has NO dependancies.
            if (depsNSTwo < depsNSOne)
            {
                return 1;
            }
            // Per default, nsOne is higher up the tree than nsTwo.
            return -1;
        }

        #endregion

        #region UTIL

        // Checks if all given types are located inside the same namespace.
        private bool AllTypesInSameNamespace(List<IRTypeNode> types)
        {
            // Get the namespace of the first type.
            var nsFirst = _TypeNSMapper[types[0]];

            // Match against all other types in the list.
            foreach (var type in types)
            {
                // if the namespace of this type is not equal to the first namespace.
                // We have found one type not contained withing the same namespace.
                var nsMatch = _TypeNSMapper[type];
                if (nsMatch != nsFirst)
                {
                    return false;
                }
            }

            return true;
        }

        // Move the given type to the new namespace, including private types.
        private void MoveType(IRTypeNode type, IRNamespace sourceNS, IRNamespace newNS)
        {
            if (type is IRClass)
            {
                var irClass = type as IRClass;
                // Remove the type from original namespace.
                sourceNS.Classes.Remove(irClass);
                // Add to new NS
                newNS.Classes.Add(irClass);

                // Also update for every private type.
                foreach (var privateType in irClass.PrivateTypes)
                {
                    MoveType(privateType, sourceNS, newNS);
                }
            }
            else if (type is IREnum)
            {
                var irEnum = type as IREnum;
                // Remove the type from original namespace.
                sourceNS.Enums.Remove(irEnum);
                // Add to new NS
                newNS.Enums.Add(irEnum);
            }

            // Private types keep their public parent as parent, public types
            // which refer to their namespace as parent will get the new 
            // namespace as parent!
            if (type.IsPrivate != true)
            {
                // Update existing parent
                type.Parent = newNS;
            }
        }
        #endregion
    }

    public class CircularException<T> : Exception
    {
        // List of namespaces that are part of a circular dependancy.
        // This list must start AND end with the same element!
        public List<T> CircularDependancies { get; }

        public CircularException(List<T> circ)
        {
            CircularDependancies = circ;
        }

        public CircularException(string message)
        : base(message)
        {
        }

        public CircularException(string message, Exception inner)
        : base(message, inner)
        {
        }
    }
}
