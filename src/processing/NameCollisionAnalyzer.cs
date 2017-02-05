using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using protoextractor.IR;

namespace protoextractor.processing
{
    // Care casing!
    // Namespace names are cased in lowercase.
    // Class types are cased in PascalCase.
    // Enum types are cased in PascalCase.
    //      Class Property names are snake_cased.
    //      Enum property names are in UPPERCASE.
    class NameCollisionAnalyzer
    {
        IRProgram _program;

        // Counter keeping track of all collisions.
        private int _collisions;

        public NameCollisionAnalyzer(IRProgram program)
        {
            _program = program;

            _collisions = 0;
        }

        public IRProgram Process()
        {
            // Check if there are not multiple types with the same shortname
            // inside each namespace.
            foreach (var ns in _program.Namespaces)
            {
                TestCollisionsWithin(ns);
            }

            // Check that subnamespaces don't collide with names of types 
            // from parent namespace.
            TestInterNSCollisions();

            return _program;
        }

        // Test for shortname collisions within one namespace.
        // This does NOT test for nested type collisions!
        private void TestCollisionsWithin(IRNamespace ns)
        {
            // Throw all names of types in one list.
            // Only select the public types, because we currently don't care about private/nested types.
            List<string> allShortNames = new List<string>();
            var classEnumeration = ns.Classes.Where(c => c.IsPrivate == false).Select(c => c.ShortName);
            var enumEnumeration = ns.Enums.Where(e => e.IsPrivate == false).Select(e => e.ShortName);

            allShortNames.AddRange(classEnumeration);
            allShortNames.AddRange(enumEnumeration);

            // Generate a set of unique elements from the collection.
            // If the amount of elements doesn't match, there is a name collision.
            var distinctSet = allShortNames.Distinct();
            if (distinctSet.Count() != allShortNames.Count())
            {
                // Solve the name collision..
                SolveCollisionsWithin(ns, allShortNames, distinctSet.ToList());
                // And rerun the test on the same namespace.
                TestCollisionsWithin(ns);
            }
        }

        // Solves name collisions between types in one namespace.
        private void SolveCollisionsWithin(IRNamespace ns, List<string> allShortNames,
            List<string> distinctShortNames)
        {
            // Find all types which collide.
            var collisions = allShortNames.GroupBy(x => x)
                                        .Where(group => group.Count() > 1)
                                        .Select(group => group.Key);
            foreach (var collisionName in collisions)
            {
                // Find all types matching the collision name.
                // NO case mismatch!
                var classesEnumeration = ns.Classes.Where(c => c.ShortName.Equals(collisionName));
                var enumEnumeration = ns.Enums.Where(e => e.ShortName.Equals(collisionName));
                // Throw them together in one list.
                List<IRTypeNode> collidedTypes = new List<IRTypeNode>();
                collidedTypes.AddRange(classesEnumeration);
                collidedTypes.AddRange(enumEnumeration);
                // Remove the first type in the list, because ONE item is allowed to remain 
                // untouched. The others need to have their name changed.
                collidedTypes.RemoveAt(0);
                // Rename collided types.
                RenameTypes(collidedTypes);
            }

        }

        private void TestInterNSCollisions()
        {
            // For each namespace, find all parent namespace.
            // Check if the parent namespaces don't have a type that is named 
            // after one of it's child namespaces.
            foreach (var ns in _program.Namespaces)
            {
                // Find parent namespaces.
                var parents = NameCollisionHelper.FindNSParents(_program, ns);
                foreach (var parentNS in parents)
                {
                    // Calculate the differrence in namespace names.
                    var parentNSNameCount = parentNS.FullName.Count();
                    var childName = ns.FullName.Substring(parentNSNameCount).Trim('.');
                    
                    // If the parent namespace has a type, with a shortname, that matches
                    // the calculated childName. That child has to be renamed!
                    // Case mismatch! PascalCase <-> lowercase
                    var foundClasses = parentNS.Classes.Where(c => c.ShortName.ToLower().Equals(childName));
                    var foundEnums = parentNS.Enums.Where(e => e.ShortName.ToLower().Equals(childName));

                    // Throw all these types together.
                    List<IRTypeNode> typeAgg = new List<IRTypeNode>();
                    typeAgg.AddRange(foundClasses);
                    typeAgg.AddRange(foundEnums);
                    // Fix names.
                    RenameTypes(typeAgg);
                }
            }
        }

        private void RenameTypes(IEnumerable<IRTypeNode> types)
        {
            foreach (var type in types)
            {
                // Prepare affix.
                var affix = string.Format("_a{0}", _collisions++);
                // Append affix to full and shortname.
                type.FullName = type.FullName + affix;
                type.ShortName = type.ShortName + affix;
            }
        }
    }

    public static class NameCollisionHelper
    {
        public static List<IRNamespace> FindNSParents(IRProgram program, IRNamespace subject)
        {
            // Return all namespaces whos fullname are found at the beginnen of the subject
            // namespace.
            var subjName = subject.FullName;
            var parents = program.Namespaces.Where(p => subjName.StartsWith(p.FullName)).ToList();
            // Remove subject, because fullname matches always with itself.
            parents.Remove(subject);
            return parents;
        }
    }
}
