
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using NDesk.Options;

namespace AddInstrumentation
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string source = null;
                string instrumentation = null;
                OptionSet o = new OptionSet
                    {
                        {"s=|source=", i => { source = i;}},
                        {"i=|instrumnetation=", i => { instrumentation = i;}},
                    };
                bool parsingError = false;
                try
                {
                    o.Parse(args);
                }
                catch (OptionException)
                {
                    parsingError = true;
                }
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(instrumentation) || parsingError)
                {
                    ShowHelp();
                    return;
                }

                Instrument(instrumentation, source);
                Console.WriteLine("Done");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("AddInstrumentation.exe -i InstrumentationAssembly -s SourceAssembly");
        }
        private static void Instrument(string instrumentation, string source)
        {
            ModuleDefMD instrumentationAssembly = ModuleDefMD.Load(instrumentation);
            TypeDef instrumentedAttribute = FindInstrumentedAttribute(instrumentationAssembly);
            ModuleDefMD sourceAssembly = ModuleDefMD.Load(source);

            List<MethodDef> methodsToProcess = GetInstrumentationMethods(instrumentationAssembly, instrumentedAttribute, sourceAssembly);

            //foreach (MethodDef method in methodsToProcess)
            //{
            //    MethodDef sourceMethod = FindCorrespondingSourceMethod(method, sourceAssembly);
            //    string action = sourceMethod == null ? "Add" : "Replace";
            //    Console.WriteLine($"{action} - {method.DeclaringType.FullName}; {method.Name}");
            //}

            foreach (MethodDef method in methodsToProcess)
            {
                TypeDef sourceType = FindSourceType(method.DeclaringType.ToTypeSig(), sourceAssembly);
                MethodDef sourceMethod = FindCorrespondingSourceMethod(method, sourceType);
                if (sourceMethod == null)
                {
                    AddMethod(method, sourceType);
                }
            }

            foreach (MethodDef method in methodsToProcess)
            {
                TypeDef sourceType = FindSourceType(method.DeclaringType.ToTypeSig(), sourceAssembly);
                MethodDef sourceMethod = FindCorrespondingSourceMethod(method, sourceType);
                CilBody iBody = method.Body;
                sourceMethod.Body = new CilBody(iBody.InitLocals, iBody.Instructions,iBody.ExceptionHandlers,iBody.Variables);
                foreach (Instruction instruction in sourceMethod.Body.Instructions)
                {
                    MemberRef mr = instruction.Operand as MemberRef;
                    if (mr != null)
                    {
                        if (mr.DeclaringType.DefinitionAssembly.FullNameToken == sourceAssembly.Assembly.FullNameToken)
                        {
                            object newOperand = instruction.Operand;
                            if (mr.IsFieldRef)
                            {
                                newOperand = LookupField(mr, sourceAssembly) ?? instruction.Operand;
                            }
                            if (mr.IsMethodRef)
                            {
                                newOperand = LookupMethod(mr, sourceAssembly) ?? instruction.Operand;
                            }
                            instruction.Operand = newOperand;
                        }
                    }

                    FieldDef fd = instruction.Operand as FieldDef;
                    if (fd != null)
                    {
                        if (fd.DeclaringType.DefinitionAssembly.FullNameToken == instrumentationAssembly.Assembly.FullNameToken)
                        {
                            instruction.Operand = LookupField(fd, sourceAssembly) ?? instruction.Operand;
                        }
                    }
                    MethodDef md = instruction.Operand as MethodDef;
                    if (md != null)
                    {
                        if (md.DeclaringType.DefinitionAssembly.FullNameToken == instrumentationAssembly.Assembly.FullNameToken)
                        {
                            instruction.Operand = LookupMethod(md, sourceAssembly) ?? instruction.Operand;
                        }
                    }

                    MethodSpec ms = instruction.Operand as MethodSpec;
                    if (ms != null)
                    {
                        for (int i = 0; i < ms.GenericInstMethodSig.GenericArguments.Count; i++)
                        {
                            TypeSig typeSig = ms.GenericInstMethodSig.GenericArguments[i];
                            if (typeSig.DefinitionAssembly.FullNameToken == instrumentationAssembly.Assembly.FullNameToken)
                            {
                                ms.GenericInstMethodSig.GenericArguments[i] = LookupType(typeSig, sourceAssembly) ?? typeSig;
                            }

                        }
                    }
                    //OpCode opcode = instruction.OpCode;
                    //object operand = instruction.Operand;
                    //string ins = instruction.ToString();
                    //Console.WriteLine(ins);
                    //Console.WriteLine(opcode);
                    //Console.WriteLine(operand ?? "null");
                    //Console.WriteLine(operand?.GetType().FullName ?? "null");
                    //Console.WriteLine("------------");
                }
                sourceMethod.Body.UpdateInstructionOffsets();
            }


            sourceAssembly.Write(Path.ChangeExtension(source,"patched"));
        }

        private static TypeSig LookupType(TypeSig typeSig, ModuleDefMD sourceAssembly)
        {
            return sourceAssembly.Types.SingleOrDefault(x => new SigComparer(SigComparerOptions.DontCompareTypeScope).Equals(x.ToTypeSig(), typeSig))?.ToTypeSig();
        }

        private static MethodDef LookupMethod(IMethodDefOrRef method, ModuleDefMD sourceAssembly)
        {
            TypeDef type = sourceAssembly.Types.SingleOrDefault(x => x.FullName == method.DeclaringType.FullName);
            return type?.FindMethod(method.Name, method.MethodSig, SigComparerOptions.PrivateScopeMethodIsComparable);
        }

        private static FieldDef LookupField(IField field, ModuleDefMD sourceAssembly)
        {
            TypeDef type = sourceAssembly.Types.SingleOrDefault(x => x.FullName == field.DeclaringType.FullName);
            return type?.FindField(field.Name, field.FieldSig, SigComparerOptions.DontCompareTypeScope);
        }
    
        private static void AddMethod(MethodDef method, TypeDef sourceType)
        {
            MethodDef md = new MethodDefUser(method.Name, method.MethodSig, method.Attributes);
            sourceType.Methods.Add(md);
        }


        private static List<MethodDef> GetInstrumentationMethods(ModuleDefMD instrumentationAssembly, TypeDef instrumentedAttribute, ModuleDefMD sourceAssembly)
        {
            return instrumentationAssembly.Types
                .Where(x => x.FullName != instrumentedAttribute.FullName)
                .Where(x => FindSourceType(x.ToTypeSig(), sourceAssembly) != null)
                .SelectMany(x => x.Methods)
                .Where(y => y.CustomAttributes.Any(x => x.AttributeType == instrumentedAttribute)).ToList();
        }

        private static TypeDef FindSourceType(TypeSig type, ModuleDefMD sourceAssembly)
        {
            return sourceAssembly.Types.SingleOrDefault(x => x.FullName == type.FullName);
        }
        private static MethodDef FindCorrespondingSourceMethod(MethodDef method, TypeDef sourceType)
        {
            return sourceType.Methods.SingleOrDefault(x => x.Name == method.Name && new SigComparer().Equals(x,method));
        }


        private static TypeDef FindInstrumentedAttribute(ModuleDefMD instrumentationAssembly)
        {
            const string attributeName = "InstrumentedAttribute";
            List<TypeDef> types = instrumentationAssembly.Types.Where(x => x.Name == attributeName).ToList();
            if (types.Count > 1)
            {
                throw new ApplicationException($"There is more than one {attributeName} in instrumenataion assembly");
            }
            if (types.Count == 0)
            {
                throw new ApplicationException($"There is no {attributeName} in instrumenataion assembly");
            }
            return types[0];
        }
    }
}
