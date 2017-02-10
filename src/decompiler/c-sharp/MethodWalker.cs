using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace protoextractor.decompiler.c_sharp
{
    class MethodWalker
    {
        // This property contains a method with the given prototype that will be triggered
        // when a call to a function is made.
        private static Action<CallInfo, List<byte>> _onCall;
        // This property contains a method with the given prototype that will be triggered
        // when a value has been assigned to a property.
        private static Action<StoreInfo, List<byte>> _onStore;

        public static void DummyOnCall(CallInfo i, List<byte> w)
        {

        }

        public static void DummyOnStore(StoreInfo i, List<byte> w)
        {

        }

        // Emulate all instructions from the given method.
        // Emulation is done through a handmade environment, including stack and written data.
        public static void WalkMethod(MethodDefinition method, Action<CallInfo, List<byte>> onCall,
            Action<StoreInfo, List<byte>> onStore)
        {
            // Store action handlers.
            _onCall = (onCall != null) ? onCall : DummyOnCall;
            _onStore = (onStore != null) ? onStore : DummyOnStore;

            // We will emulate the given method, this means running each statement 
            // with a program state for the moment that instruction would be executed.
            List<OpState> processing = new List<OpState>();
            // Start by processing the first operation.
            processing.Add(new OpState()
            {
                BytesWritten = new List<byte>(),
                Conditions = new List<Condition>(),
                Offset = 0,
                Stack = new List<object>(),
            });

            // Keep track of all operations we already processed. This avoids stack overflowing
            // and circular processing. The actual value that is recorded is the operation OFFSET.
            List<int> processed = new List<int>();

            while (processing.Count > 0)
            {
                /* List.Sort is not stable.. so unexpected things COULD happen */
                // Sort by conditions ASCENDING.
                processing.Sort((a, b) => a.Conditions.Count - b.Conditions.Count);
                // Sort by offset ASCENDING.
                processing.Sort((a, b) => a.Offset - b.Offset);
                // Remove and process the next operation to emulate.
                var next = processing.First();
                processing.Remove(next);
                // Annihilate the conditions from any branch joins.
                while (processing.Any() && processing.First().Offset == next.Offset)
                {
                    var joinOp = processing.First();
                    processing.Remove(joinOp);
                    var deadConds = next.Conditions.Where(c =>
                        joinOp.Conditions.Any(c2 => c2.Offset == c.Offset)).ToList();
                    foreach (var c in deadConds)
                    {
                        next.Conditions.Remove(c);
                    }
                }
                Explore(method, processing, processed, next);
            }
        }

        // Emulate one specific operation from the given method.
        private static void Explore(MethodDefinition methodDef, List<OpState> processingQueue, List<int> processedOps, OpState operation)
        {
            // Get specific values from current operation;
            int offset = operation.Offset;
            // Do not simulate processed instructions again.
            var ins = methodDef.Body.Instructions.First(o => o.Offset == offset);
            if (processedOps.Contains(ins.Offset)) return;
            processedOps.Add(ins.Offset);

            List<object> stack = operation.Stack;
            List<Condition> conditions = operation.Conditions;
            List<byte> writtenBytes = operation.BytesWritten;

            switch (ins.OpCode.Code)
            {
                case Code.Ldnull:
                    stack.Add("null");
                    break;
                case Code.Ldc_I4:
                    // The operand is a normal int.
                    stack.Add(ins.Operand);
                    break;
                case Code.Ldc_I4_S:
                    // Combine 0 and the sbyte to force a conversion.
                    // The operand is a signed byte.
                    int intVal = 0 | (sbyte)ins.Operand;
                    stack.Add(intVal);
                    break;
                case Code.Ldc_R4:
                    float flVal = (float)ins.Operand;
                    stack.Add(flVal);
                    break;
                case Code.Ldstr:
                    stack.Add(ins.Operand);
                    break;
                case Code.Ldc_I4_0:
                case Code.Ldc_I4_1:
                case Code.Ldc_I4_2:
                case Code.Ldc_I4_3:
                case Code.Ldc_I4_4:
                case Code.Ldc_I4_5:
                case Code.Ldc_I4_6:
                case Code.Ldc_I4_7:
                case Code.Ldc_I4_8:
                    // OP2 is the second byte representing our opcode.
                    // Ldc_i4_0 starts at OP2 = 0x16, so we subtract 0x16 from OP2 for each
                    // matching opcode to find the index of the argument.
                    int value = (int)(ins.OpCode.Op2 - 0x16);
                    stack.Add(value);
                    break;
                case Code.Ldc_I4_M1:
                    // Pushes integer value -1 onto the stack.
                    stack.Add(-1);
                    break;
                case Code.Ldloca:
                    stack.Add("&" + (ins.Operand as VariableReference).ToString());
                    break;
                case Code.Ldloc:
                case Code.Ldloc_S:
                case Code.Ldloca_S:
                    stack.Add((ins.Operand as VariableReference).ToString());
                    break;
                case Code.Ldloc_0:
                case Code.Ldloc_1:
                case Code.Ldloc_2:
                case Code.Ldloc_3:
                    // LdLoc_0 starts at 0x06 for OP2.
                    int localIdx = (int)(ins.OpCode.Op2 - 0x06);
                    stack.Add(string.Format("ldArg{0}", localIdx));
                    break;
                case Code.Ldfld:
                    var field = String.Format("{0}.{1}",
                        stack.Pop(), (ins.Operand as FieldReference).Name);
                    stack.Add(field);
                    break;
                case Code.Ldsfld:
                    stack.Add((ins.Operand as FieldReference).FullName);
                    break;
                case Code.Ldarg:
                    {
                        var idx = (ins.Operand as ParameterReference).Index;
                        if (idx == -1)
                        {
                            stack.Add("this");
                        }
                        else
                        {
                            stack.Add(String.Format("arg{0}", idx));
                        }
                    }
                    break;
                case Code.Ldarg_0:
                case Code.Ldarg_1:
                case Code.Ldarg_2:
                case Code.Ldarg_3:
                case Code.Ldarg_S:
                    {
                        // OP2 is the second byte representing our opcode.
                        // Ldarg_0 starts at OP2 = 2, so we subtract 2 from OP2 for each
                        // matching opcode to find the index of the argument.
                        stack.Add(String.Format("arg{0}", (ins.OpCode.Op2 - 2).ToString()));
                    }
                    break;
                case Code.Ldelem_Ref:
                    {
                        var idx = stack.Pop();
                        var arr = stack.Pop();
                        stack.Add(String.Format("{0}[{1}]", arr, idx));
                    }
                    break;
                case Code.Ldftn:
                    stack.Add(String.Format("&({0})", ins.Operand));
                    break;
                case Code.Newobj:
                    {
                        var mr = ins.Operand as MethodReference;
                        var numParam = mr.Parameters.Count;
                        var stackIdx = stack.Count - numParam;
                        var args = stack.GetRange(stackIdx, numParam);
                        stack.RemoveRange(stackIdx, numParam);
                        var callString = String.Format("new {0}({1})",
                            mr.DeclaringType.Name, String.Join(", ", args));
                        stack.Add(callString);
                        args.Insert(0, "this");

                        var info = new CallInfo
                        {
                            Conditions = new List<Condition>(conditions),
                            Method = mr,
                            Arguments = args,
                            String = callString
                        };
                        // Process data collected up until now
                        _onCall(info, writtenBytes);
                    }
                    break;
                case Code.Newarr:
                    stack.Add(String.Format("new {0}[{1}]",
                        (ins.Operand as TypeDefinition).FullName, stack.Pop()));
                    break;
                case Code.Brfalse:
                    {
                        var lhs = stack.Pop().ToString();
                        var src = ins.Offset;
                        var tgt = (ins.Operand as Instruction).Offset;
                        var cond = new Condition(src, lhs, Comparison.IsFalse);
                        var ncond = new Condition(src, lhs, Comparison.IsTrue);
                        Branch(tgt, operation, cond, ncond, processingQueue);
                    }
                    break;
                case Code.Brtrue:
                    {
                        var lhs = stack.Pop().ToString();
                        var src = ins.Offset;
                        var tgt = (ins.Operand as Instruction).Offset;
                        var cond = new Condition(src, lhs, Comparison.IsTrue);
                        var ncond = new Condition(src, lhs, Comparison.IsFalse);
                        Branch(tgt, operation, cond, ncond, processingQueue);
                    }
                    break;
                case Code.Beq:
                case Code.Bne_Un:
                case Code.Ble:
                case Code.Bge:
                case Code.Blt:
                case Code.Bgt:
                    {
                        var rhs = stack.Pop().ToString();
                        var lhs = stack.Pop().ToString();
                        var src = ins.Offset;
                        var tgt = (ins.Operand as Instruction).Offset;
                        Condition cond = null, ncond = null;
                        switch (ins.OpCode.Code)
                        {
                            case Code.Beq:
                                cond = new Condition(src, lhs, Comparison.Equal, rhs);
                                ncond = new Condition(src, lhs, Comparison.Inequal, rhs);
                                break;
                            case Code.Bne_Un:
                                cond = new Condition(src, lhs, Comparison.Inequal, rhs);
                                ncond = new Condition(src, lhs, Comparison.Equal, rhs);
                                break;
                            case Code.Ble:
                                // x <= y --> y >= x; !(x <= y) --> x > y
                                cond = new Condition(src, rhs, Comparison.GreaterThanEqual, lhs);
                                ncond = new Condition(src, lhs, Comparison.GreaterThan, rhs);
                                break;
                            case Code.Bge:
                                cond = new Condition(src, lhs, Comparison.GreaterThanEqual, rhs);
                                // !(x >= y) --> y > x
                                ncond = new Condition(src, rhs, Comparison.GreaterThan, lhs);
                                break;
                            case Code.Blt:
                                // x < y --> y > x; !(x < y) --> x >= y
                                cond = new Condition(src, rhs, Comparison.GreaterThan, lhs);
                                ncond = new Condition(src, lhs, Comparison.GreaterThanEqual, rhs);
                                break;
                            case Code.Bgt:
                                // !(x > y) --> y >= x
                                cond = new Condition(src, lhs, Comparison.GreaterThan, rhs);
                                ncond = new Condition(src, rhs, Comparison.GreaterThanEqual, lhs);
                                break;
                        }
                        Branch(tgt, operation, cond, ncond, processingQueue);
                    }
                    break;
                case Code.Br:
                    // Jump to other location (unconditionally).
                    operation.Offset = (ins.Operand as Instruction).Offset;
                    Explore(methodDef, processingQueue, processedOps, operation);
                    return;
                case Code.Stsfld:
                    {
                        var arg = stack.Pop().ToString();
                        // Don't pop for the object pointer, because this is a static 
                        // set.
                        var info = new StoreInfo
                        {
                            Conditions = new List<Condition>(conditions),
                            Field = ins.Operand as FieldReference,
                            Argument = arg,
                        };
                        _onStore(info, null);
                    }
                    break;
                case Code.Stfld:
                    {
                        var arg = stack.Pop().ToString();
                        /*var obj = */
                        stack.Pop();
                        var info = new StoreInfo
                        {
                            Conditions = new List<Condition>(conditions),
                            Field = ins.Operand as FieldReference,
                            Argument = arg,
                        };
                        _onStore(info, null);
                    }
                    break;
                case Code.Stelem_Ref:
                    {
                        /*var val = */
                        stack.Pop();
                        /*var idx = */
                        stack.Pop();
                        /*var arr = */
                        stack.Pop();
                    }
                    break;
                case Code.Mul:
                    {
                        var rhs = stack.Pop().ToString();
                        var lhs = stack.Pop().ToString();
                        stack.Add(String.Format("{0} * {1}", lhs, rhs));
                    }
                    break;
                case Code.Call:
                case Code.Callvirt:
                    {
                        var mr = ins.Operand as MethodReference;
                        var args = new List<object>();
                        for (var i = 0; i < mr.Parameters.Count; i++)
                        {
                            args.Add(stack.Pop());
                        }
                        if (mr.HasThis)
                        {
                            if (stack.Count < 1)
                            {
                                Console.WriteLine("err");
                            }
                            args.Add(stack.Pop());
                        }
                        args.Reverse();
                        var callString = String.Format("{0}.{1}({2})",
                            mr.HasThis ? args.First().ToString() : mr.DeclaringType.Name,
                            mr.Name,
                            String.Join(", ",
                                mr.HasThis ? args.Skip(1) : args));
                        if (mr.ReturnType.FullName != "System.Void")
                        {
                            stack.Add(callString);
                        }

                        var info = new CallInfo
                        {
                            Conditions = new List<Condition>(conditions),
                            Method = mr,
                            Arguments = args,
                            String = callString
                        };
                        // Process data collected up until now
                        _onCall(info, writtenBytes);
                    }
                    break;
            }

            if (ins.Next != null)
            {
                // Update the current operation with next offset and requeue.
                operation.Offset = ins.Next.Offset;
                processingQueue.Add(operation);
            }
        }

        // Fill in information about the processed branching operation.
        // We have to evaluate the operation for which the condition evaluated true AND the operation
        // for which the condition evaluated false.
        private static void Branch(int target, OpState operation, Condition conditionTaken, Condition conditionNotTaken,
            List<OpState> processing)
        {
            // Make a copy of the previous conditions.
            var newConds = new List<Condition>(operation.Conditions);
            // Make a copy os the current stack.
            var newStack = new List<object>(operation.Stack);
            // Make a copy of the written bytes.
            var newWritten = new List<byte>(operation.BytesWritten);

            // The new opstate will contain the chosen path.
            // Condition results create the path we took.
            newConds.Add(conditionTaken);
            var newOpState = new OpState()
            {
                Offset = target,
                Conditions = newConds,
                Stack = newStack,
                BytesWritten = newWritten
            };
            // Queue the new operation
            processing.Add(newOpState);

            // The current opstate will take the other path.
            operation.Conditions.Add(conditionNotTaken);
        }
    }

    // Meta information for execution of a certain operation.
    class OpState
    {
        // This class is used to evaluate a CERTAIN operation statement. This statement is a
        // part of the bytecode of the method that is inspected (see MethodWalker.Walk)

        // Index of the specific operation, starting from the beginning of the method body.
        public int Offset { get; set; }
        // Built up stack which is available to this operation.
        public List<object> Stack { get; set; }
        // Conditions that were evaluated in order to reach the operation.
        public List<Condition> Conditions { get; set; }
        // Explicit bytes written to get to this operation.
        public List<byte> BytesWritten { get; set; }
    }

    public class CallInfo
    {
        public List<Condition> Conditions { get; set; }
        public MethodReference Method { get; set; }
        public List<object> Arguments { get; set; }
        public string String { get; set; }
        public List<byte> BytesWritten { get; set; }
    }

    public class StoreInfo
    {
        public List<Condition> Conditions { get; set; }
        public FieldReference Field { get; set; }
        public string Argument { get; set; }
    }

    public enum Comparison
    {
        Equal,
        Inequal,
        GreaterThan,
        GreaterThanEqual,
        IsTrue,
        IsFalse
    }

    public class Condition
    {
        public int Offset { get; set; }
        public string Lhs { get; set; }
        public string Rhs { get; set; }
        public Comparison Cmp { get; set; }

        public Condition(int offset, string lhs, Comparison cmp, string rhs = null)
        {
            Offset = offset;
            Lhs = lhs;
            Rhs = rhs;
            Cmp = cmp;
        }

        public override string ToString()
        {
            var cmpStr = "";
            switch (Cmp)
            {
                case Comparison.Equal: cmpStr = "=="; break;
                case Comparison.Inequal: cmpStr = "!="; break;
                case Comparison.GreaterThan: cmpStr = ">"; break;
                case Comparison.GreaterThanEqual: cmpStr = ">="; break;
                case Comparison.IsTrue: cmpStr = "== true"; break;
                case Comparison.IsFalse: cmpStr = "== false"; break;
            }
            return String.Format("{0} {1}{2}", Lhs, cmpStr,
                String.IsNullOrEmpty(Rhs) ? "" : " " + Rhs);
        }
    }

    public static class ListExtensions
    {
        /* 
         * List<T> is a more versatile stack than Stack<T>. 
         * We implement the Stack interface on List because normal cast is somehow not possible?!
         */

        // Pop behaviour is to remove and return the highest index item from the list.
        public static T Pop<T>(this List<T> stack)
        {
            // 0 - indexed
            var lastIdx = stack.Count - 1;
            var result = stack[lastIdx];
            stack.RemoveAt(lastIdx);
            return result;
        }

        // Peek is similar to Pop, but does not remove the item from the list.
        public static T Peek<T>(this List<T> stack)
        {
            // 0 - indexed
            var lastIdx = stack.Count - 1;
            var result = stack[lastIdx];
            return result;
        }

        // Push adds the item to the list after the highest indexed item.
        // The collection space is expanded if needed.
        public static void Push<T>(this List<T> stack, T item)
        {
            stack.Add(item);
        }

        //public static TypeName PackageName(this TypeReference type)
        //{
        //    var types = new List<string> { type.Name };
        //    while (type.DeclaringType != null)
        //    {
        //        type = type.DeclaringType;
        //        var tName = type.Name;
        //        if (tName == "Types") continue;
        //        types.Add(tName);
        //    }
        //    types.Reverse();
        //    return new TypeName(type.Namespace, String.Join(".", types));
        //}
    }
}
