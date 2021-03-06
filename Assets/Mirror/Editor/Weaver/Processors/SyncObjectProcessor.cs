using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mirror.Weaver
{
    public class SyncObjectProcessor
    {
        readonly List<FieldDefinition> syncObjects = new List<FieldDefinition>();

        /// <summary>
        /// Finds SyncObjects fields in a type
        /// <para>Type should be a NetworkBehaviour</para>
        /// </summary>
        /// <param name="td"></param>
        /// <returns></returns>
        public void ProcessSyncObjects(TypeDefinition td)
        {
            foreach (FieldDefinition fd in td.Fields)
            {
                if (fd.FieldType.Resolve().ImplementsInterface<ISyncObject>())
                {
                    if (fd.IsStatic)
                    {
                        Weaver.Error($"{fd.Name} cannot be static", fd);
                        continue;
                    }

                    GenerateReadersAndWriters(fd.FieldType);

                    syncObjects.Add(fd);
                }
            }

            RegisterSyncObjects(td);
        }

        /// <summary>
        /// Generates serialization methods for synclists
        /// </summary>
        /// <param name="td">The synclist class</param>
        /// <param name="mirrorBaseType">the base SyncObject td inherits from</param>
        static void GenerateReadersAndWriters(TypeReference tr)
        {
            if (tr is GenericInstanceType genericInstance)
            {
                foreach (TypeReference argument in genericInstance.GenericArguments)
                {
                    if (!argument.IsGenericParameter)
                    {
                        Readers.GetReadFunc(argument);
                        Writers.GetWriteFunc(argument);
                    }
                }
            }

            if (tr != null)
            {
                GenerateReadersAndWriters(tr.Resolve().BaseType);
            }
        }

        void RegisterSyncObjects(TypeDefinition netBehaviourSubclass)
        {
            Weaver.DLog(netBehaviourSubclass, "  GenerateConstants ");

            // find instance constructor
            MethodDefinition ctor = netBehaviourSubclass.GetMethod(".ctor");

            if (ctor == null)
            {
                Weaver.Error($"{netBehaviourSubclass.Name} has invalid constructor", netBehaviourSubclass);
                return;
            }

            Instruction ret = ctor.Body.Instructions[ctor.Body.Instructions.Count - 1];
            if (ret.OpCode == OpCodes.Ret)
            {
                ctor.Body.Instructions.RemoveAt(ctor.Body.Instructions.Count - 1);
            }
            else
            {
                Weaver.Error($"{netBehaviourSubclass.Name} has invalid constructor", ctor);
                return;
            }

            ILProcessor ctorWorker = ctor.Body.GetILProcessor();

            foreach (FieldDefinition fd in syncObjects)
            {
                GenerateSyncObjectRegistration(ctorWorker, fd);
            }

            // finish ctor
            ctorWorker.Append(ctorWorker.Create(OpCodes.Ret));

            // in case class had no cctor, it might have BeforeFieldInit, so injected cctor would be called too late
            netBehaviourSubclass.Attributes &= ~TypeAttributes.BeforeFieldInit;
        }

        public static bool ImplementsSyncObject(TypeReference typeRef)
        {
            try
            {
                // value types cant inherit from SyncObject
                if (typeRef.IsValueType)
                {
                    return false;
                }

                return typeRef.Resolve().ImplementsInterface<ISyncObject>();
            }
            catch
            {
                // sometimes this will fail if we reference a weird library that can't be resolved, so we just swallow that exception and return false
            }

            return false;
        }

        /*
            // generates code like:
            this.InitSyncObject(m_sizes);
        */
        static void GenerateSyncObjectRegistration(ILProcessor worker, FieldDefinition fd)
        {
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Ldarg_0));
            worker.Append(worker.Create(OpCodes.Ldfld, fd));

            MethodReference initSyncObjectRef = worker.Body.Method.Module.ImportReference<NetworkBehaviour>(nb => nb.InitSyncObject(default));
            worker.Append(worker.Create(OpCodes.Call, initSyncObjectRef));
        }
    }
}
