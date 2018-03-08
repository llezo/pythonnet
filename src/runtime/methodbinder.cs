using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Python.Runtime
{
    /// <summary>
    /// A MethodBinder encapsulates information about a (possibly overloaded)
    /// managed method, and is responsible for selecting the right method given
    /// a set of Python arguments. This is also used as a base class for the
    /// ConstructorBinder, a minor variation used to invoke constructors.
    /// </summary>
    internal class MethodBinder
    {
        private readonly ArrayList _methodArrayList = new ArrayList();
        private MethodBase[] _sortedMethodArray;
        private bool _isInitialized;
        public bool AllowThreads = true;

        internal MethodBinder(){}

        internal MethodBinder(MethodInfo methodInfo)
        {
            _methodArrayList.Add(methodInfo);
        }

        public int Count => _methodArrayList.Count;

        internal void AddMethod(MethodBase methodBase)
        {
            _methodArrayList.Add(methodBase);
        }

        /// <summary>
        /// Given a sequence of MethodInfo and a sequence of types, return the
        /// MethodInfo that matches the signature represented by those types.
        /// </summary>
        internal static MethodInfo MatchSignature(IEnumerable<MethodInfo> methodInfos, Type[] typeArray)
        {
            if (typeArray == null) return null;

            var typeArrayLength = typeArray.Length;
            /* OLD code replaced by the LINQ operation below
            foreach (var methodInfo in methodInfos)
            {
                var parameterInfos = methodInfo.GetParameters();
                var parameterInfosLength = parameterInfos.Length;

                if (parameterInfosLength != typeArrayLength)
                {
                    continue;
                }

                 for (var n = 0; n < parameterInfos.Length; n++)
                {
                    if (typeArray[n] != parameterInfos[n].ParameterType)
                    {
                        break;
                    }
                    if (n == parameterInfos.Length - 1)
                    {
                        return methodInfo;
                    }
                }
            }
            */
            //Made a LINQ operation because we're just comparing and returning the first methodInfo available (or null)
            return (from methodInfo in methodInfos
                let parameterInfos = methodInfo.GetParameters()
                let parameterInfosLength = parameterInfos.Length
                where parameterInfosLength == typeArrayLength
                where parameterInfos
                    .TakeWhile((parameterInfo, n) => typeArray[n] == parameterInfo.ParameterType)
                    .Where((parameterInfo, n) => n == parameterInfosLength - 1)
                    .Any()
                select methodInfo).FirstOrDefault();
        }

        /// <summary>
        /// Given a sequence of MethodInfo and a sequence of type parameters,
        /// return the MethodInfo that represents the matching closed generic.
        /// </summary>
        internal static MethodInfo MatchParameters(MethodInfo[] mi, Type[] tp)
        {
            if (tp == null)
            {
                return null;
            }
            int count = tp.Length;
            foreach (MethodInfo t in mi)
            {
                if (!t.IsGenericMethodDefinition)
                {
                    continue;
                }
                Type[] args = t.GetGenericArguments();
                if (args.Length != count)
                {
                    continue;
                }
                return t.MakeGenericMethod(tp);
            }
            return null;
        }


        /// <summary>
        /// Given a sequence of MethodInfo and two sequences of type parameters,
        /// return the MethodInfo that matches the signature and the closed generic.
        /// </summary>
        internal static MethodInfo MatchSignatureAndParameters(MethodInfo[] mi, Type[] genericTp, Type[] sigTp)
        {
            if (genericTp == null || sigTp == null)
            {
                return null;
            }
            int genericCount = genericTp.Length;
            int signatureCount = sigTp.Length;
            foreach (MethodInfo t in mi)
            {
                if (!t.IsGenericMethodDefinition)
                {
                    continue;
                }
                Type[] genericArgs = t.GetGenericArguments();
                if (genericArgs.Length != genericCount)
                {
                    continue;
                }
                ParameterInfo[] pi = t.GetParameters();
                if (pi.Length != signatureCount)
                {
                    continue;
                }
                for (var n = 0; n < pi.Length; n++)
                {
                    if (sigTp[n] != pi[n].ParameterType)
                    {
                        break;
                    }
                    if (n == pi.Length - 1)
                    {
                        MethodInfo match = t;
                        if (match.IsGenericMethodDefinition)
                        {
                            // FIXME: typeArgs not used
                            Type[] typeArgs = match.GetGenericArguments();
                            return match.MakeGenericMethod(genericTp);
                        }
                        return match;
                    }
                }
            }
            return null;
        }


        /// <summary>
        /// Return the array of MethodInfo for this method. The result array
        /// is arranged in order of precedence (done lazily to avoid doing it
        /// at all for methods that are never called).
        /// </summary>
        internal MethodBase[] GetMethods()
        {
            if (!_isInitialized)
            {
                // I'm sure this could be made more efficient.
                _methodArrayList.Sort(new MethodSorter());
                _sortedMethodArray = (MethodBase[])_methodArrayList.ToArray(typeof(MethodBase));
                _isInitialized = true;
            }
            return _sortedMethodArray;
        }

        /// <summary>
        /// Precedence algorithm largely lifted from Jython - the concerns are
        /// generally the same so we'll start with this and tweak as necessary.
        /// </summary>
        /// <remarks>
        /// Based from Jython `org.python.core.ReflectedArgs.precedence`
        /// See: https://github.com/jythontools/jython/blob/master/src/org/python/core/ReflectedArgs.java#L192
        /// </remarks>
        internal static int GetPrecedence(MethodBase mi)
        {
            ParameterInfo[] pi = mi.GetParameters();
            int val = mi.IsStatic ? 3000 : 0;
            int num = pi.Length;

            val += mi.IsGenericMethod ? 1 : 0;
            for (var i = 0; i < num; i++)
            {
                val += ArgPrecedence(pi[i].ParameterType);
            }

            return val;
        }

        /// <summary>
        /// Return a precedence value for a particular Type object.
        /// </summary>
        internal static int ArgPrecedence(Type t)
        {
            Type objectType = typeof(object);
            if (t == objectType)
            {
                return 3000;
            }

            TypeCode tc = Type.GetTypeCode(t);
            // TODO: Clean up
            switch (tc)
            {
                case TypeCode.Object:
                    return 1;

                case TypeCode.UInt64:
                    return 10;

                case TypeCode.UInt32:
                    return 11;

                case TypeCode.UInt16:
                    return 12;

                case TypeCode.Int64:
                    return 13;

                case TypeCode.Int32:
                    return 14;

                case TypeCode.Int16:
                    return 15;

                case TypeCode.Char:
                    return 16;

                case TypeCode.SByte:
                    return 17;

                case TypeCode.Byte:
                    return 18;

                case TypeCode.Single:
                    return 20;

                case TypeCode.Double:
                    return 21;

                case TypeCode.String:
                    return 30;

                case TypeCode.Boolean:
                    return 40;
            }

            if (t.IsArray)
            {
                Type e = t.GetElementType();
                if (e == objectType)
                {
                    return 2500;
                }
                return 100 + ArgPrecedence(e);
            }

            return 2000;
        }

        /// <summary>
        /// Bind the given Python instance and arguments to a particular method
        /// overload and return a structure that contains the converted Python
        /// instance, converted arguments and the correct method to call.
        /// </summary>
        internal Binding Bind(IntPtr inst, IntPtr pythonParametersPtr, IntPtr kw)
        {
            return Bind(inst, pythonParametersPtr, kw, null, null);
        }

        internal Binding Bind(IntPtr inst, IntPtr pythonParametersPtr, IntPtr kw, MethodBase info)
        {
            return Bind(inst, pythonParametersPtr, kw, info, null);
        }

        internal Binding Bind(IntPtr inst, IntPtr pythonParametersPtr, IntPtr kw, MethodBase info, MethodInfo[] methodInfoArray)
        {
            // loop to find match, return invoker w/ or /wo error
            MethodBase[] _methods = null;
            // NOTE: return the size of the args pointer (the number of parameter from the python call)
            int pythonParameterCount = Runtime.PyTuple_Size(pythonParametersPtr);
            //WHY: Why is that so widely scoped ?
            object pythonManagedParameterPtr;
            var isGeneric = false;
            ArrayList defaultParameterList = null;
            if (info != null)
            {
                // NOTE: If a MethodBase object has been provided, create an array with only it.
                // WHY a method base is provided some times and not other ? (ex: when call a genric with a type in [])
                _methods = new MethodBase[1];
                _methods.SetValue(info, 0);
            }
            else
            {
                // NOTE: Create an array of MethodBase from teh called method/constructor
                // WHY not use the methodinfo provided?
                _methods = GetMethods();
            }
            //WHY is it so widely scoped
            Type clrConvertedParameterType;
            // TODO: Clean up
            foreach (MethodBase methodInfo in _methods)
            {
                //WHY not just do isGeneric = mi.IsGenericMethod or use it directly ?
                if (methodInfo.IsGenericMethod)
                {
                    isGeneric = true;
                }
                //NOTE: Get the parameter from the current MethodBase
                //NOTE: MethodInfo Ok
                ParameterInfo[] clrParameterInfoArray = methodInfo.GetParameters();
                //NOTE: Get the number of clr parameters
                int clrParameterCount = clrParameterInfoArray.Length;

                var paramCountMatch = false;
                //REFACTOR: Use var like the other local variables
                var clrHasParamArray = false;
                var clrParamsArrayStart = -1;

                var byRefCount = 0;

                if (pythonParameterCount == clrParameterCount)
                {
                    paramCountMatch = true;
                }
                else if (pythonParameterCount < clrParameterCount)
                {
                    paramCountMatch = true;
                    defaultParameterList = new ArrayList();
                    for (int v = pythonParameterCount; v < clrParameterCount; v++)
                    {
                        if (clrParameterInfoArray[v].DefaultValue == DBNull.Value)
                        {
                            paramCountMatch = false;
                        }
                        else
                        {
                            defaultParameterList.Add(clrParameterInfoArray[v].DefaultValue);
                        }
                    }
                }
                else if (pythonParameterCount > clrParameterCount && clrParameterCount > 0 &&
                         Attribute.IsDefined(clrParameterInfoArray[clrParameterCount - 1], typeof(ParamArrayAttribute)))
                {
                    // This is a `foo({...}, params object[] bar)` style method
                    paramCountMatch = true;
                    clrHasParamArray = true;
                    clrParamsArrayStart = clrParameterCount - 1;
                }

                if (paramCountMatch)
                {
                    var methodParametersPtrArray = new object[clrParameterCount];

                    for (var n = 0; n < clrParameterCount; n++)
                    {
                        IntPtr pythonParameterPtr;
                        if (n < pythonParameterCount)
                        {
                            if (clrParamsArrayStart == n)
                            {
                                // map remaining Python arguments to a tuple since
                                // the managed function accepts it - hopefully :]
                                //WHY: Hmmm it returns a lot of python paramter as one. isn't there a problem later ?
                                pythonParameterPtr = Runtime.PyTuple_GetSlice(pythonParametersPtr, clrParamsArrayStart, pythonParameterCount);
                            }
                            else
                            {
                                pythonParameterPtr = Runtime.PyTuple_GetItem(pythonParametersPtr, n);
                            }

                            // this logic below handles cases when multiple overloading methods
                            // are ambiguous, hence comparison between Python and CLR types
                            // is necessary
                            clrConvertedParameterType = null;
                            IntPtr pythonParameterPtrType = IntPtr.Zero;
                            if (_methods.Length > 1)
                            {
                                pythonParameterPtrType = Runtime.PyObject_Type(pythonParameterPtr);
                                Exceptions.Clear();
                                if (pythonParameterPtrType != IntPtr.Zero)
                                {
                                    clrConvertedParameterType = Converter.GetTypeByAlias(pythonParameterPtrType);
                                }
                                Runtime.XDecref(pythonParameterPtrType);
                            }


                            if (clrConvertedParameterType != null)
                            {
                                var parameterTypeMatch = false;
                                if ((clrParameterInfoArray[n].ParameterType != typeof(object)) && (clrParameterInfoArray[n].ParameterType != clrConvertedParameterType))
                                {
                                    IntPtr pythonConvertedParameterPtrType = Converter.GetPythonTypeByAlias(clrParameterInfoArray[n].ParameterType);
                                    pythonParameterPtrType = Runtime.PyObject_Type(pythonParameterPtr);
                                    Exceptions.Clear();
                                    if (pythonParameterPtrType != IntPtr.Zero)
                                    {
                                        if (pythonConvertedParameterPtrType != pythonParameterPtrType)
                                        {
                                            parameterTypeMatch = false;
                                        }
                                        else
                                        {
                                            parameterTypeMatch = true;
                                            clrConvertedParameterType = clrParameterInfoArray[n].ParameterType;
                                        }
                                    }
                                    if (!parameterTypeMatch)
                                    {
                                        // this takes care of enum values
                                        TypeCode clrParameterTypeCode = Type.GetTypeCode(clrParameterInfoArray[n].ParameterType);
                                        TypeCode clrConvertedParameterTypeCode = Type.GetTypeCode(clrConvertedParameterType);
                                        if (clrParameterTypeCode == clrConvertedParameterTypeCode)
                                        {
                                            parameterTypeMatch = true;
                                            clrConvertedParameterType = clrParameterInfoArray[n].ParameterType;
                                        }
                                    }
                                    Runtime.XDecref(pythonParameterPtrType);
                                    if (!parameterTypeMatch)
                                    {
                                        methodParametersPtrArray = null;
                                        break;
                                    }
                                }
                                else
                                {
                                    parameterTypeMatch = true;
                                    clrConvertedParameterType = clrParameterInfoArray[n].ParameterType;
                                }
                            }
                            else
                            {
                                clrConvertedParameterType = clrParameterInfoArray[n].ParameterType;
                            }

                            if (clrParameterInfoArray[n].IsOut || clrConvertedParameterType.IsByRef)
                            {
                                byRefCount++;
                            }

                            if (!Converter.ToManaged(pythonParameterPtr, clrConvertedParameterType, out pythonManagedParameterPtr, false))
                            {
                                Exceptions.Clear();
                                methodParametersPtrArray = null;
                                break;
                            }
                            if (clrParamsArrayStart == n)
                            {
                                // GetSlice() creates a new reference but GetItem()
                                // returns only a borrow reference.
                                Runtime.XDecref(pythonParameterPtr);
                            }
                            methodParametersPtrArray[n] = pythonParameterPtr;
                        }
                        else
                        {
                            if (defaultParameterList != null)
                            {
                                methodParametersPtrArray[n] = defaultParameterList[n - pythonParameterCount];
                            }
                        }
                    }

                    if (methodParametersPtrArray == null)
                    {
                        continue;
                    }

                    object target = null;
                    if (!methodInfo.IsStatic && inst != IntPtr.Zero)
                    {
                        //CLRObject co = (CLRObject)ManagedType.GetManagedObject(inst);
                        // InvalidCastException: Unable to cast object of type
                        // 'Python.Runtime.ClassObject' to type 'Python.Runtime.CLRObject'
                        var co = ManagedType.GetManagedObject(inst) as CLRObject;

                        // Sanity check: this ensures a graceful exit if someone does
                        // something intentionally wrong like call a non-static method
                        // on the class rather than on an instance of the class.
                        // XXX maybe better to do this before all the other rigmarole.
                        if (co == null)
                        {
                            return null;
                        }
                        target = co.inst;
                    }

                    return new Binding(methodInfo, target, methodParametersPtrArray, byRefCount);
                }
            }
            // We weren't able to find a matching method but at least one
            // is a generic method and info is null. That happens when a generic
            // method was not called using the [] syntax. Let's introspect the
            // type of the arguments and use it to construct the correct method.
            if (isGeneric && info == null && methodInfoArray != null)
            {
                Type[] types = Runtime.PythonArgsToTypeArray(pythonParametersPtr, true);
                MethodInfo mi = MatchParameters(methodInfoArray, types);
                return Bind(inst, pythonParametersPtr, kw, mi, null);
            }
            return null;
        }

        internal virtual IntPtr Invoke(IntPtr inst, IntPtr args, IntPtr kw)
        {
            return Invoke(inst, args, kw, null, null);
        }

        internal virtual IntPtr Invoke(IntPtr inst, IntPtr args, IntPtr kw, MethodBase info)
        {
            return Invoke(inst, args, kw, info, null);
        }

        internal virtual IntPtr Invoke(IntPtr inst, IntPtr args, IntPtr kw, MethodBase info, MethodInfo[] methodinfo)
        {
            Binding binding = Bind(inst, args, kw, info, methodinfo);
            object result;
            IntPtr ts = IntPtr.Zero;

            if (binding == null)
            {
                Exceptions.SetError(Exceptions.TypeError, "No method matches given arguments");
                return IntPtr.Zero;
            }

            if (AllowThreads)
            {
                ts = PythonEngine.BeginAllowThreads();
            }

            try
            {
                result = binding.info.Invoke(binding.inst, BindingFlags.Default, null, binding.args, null);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                {
                    e = e.InnerException;
                }
                if (AllowThreads)
                {
                    PythonEngine.EndAllowThreads(ts);
                }
                Exceptions.SetError(e);
                return IntPtr.Zero;
            }

            if (AllowThreads)
            {
                PythonEngine.EndAllowThreads(ts);
            }

            // If there are out parameters, we return a tuple containing
            // the result followed by the out parameters. If there is only
            // one out parameter and the return type of the method is void,
            // we return the out parameter as the result to Python (for
            // code compatibility with ironpython).

            var mi = (MethodInfo)binding.info;

            if (binding.outs == 1 && mi.ReturnType == typeof(void))
            {
            }

            if (binding.outs > 0)
            {
                ParameterInfo[] pi = mi.GetParameters();
                int c = pi.Length;
                var n = 0;

                IntPtr t = Runtime.PyTuple_New(binding.outs + 1);
                IntPtr v = Converter.ToPython(result, mi.ReturnType);
                Runtime.PyTuple_SetItem(t, n, v);
                n++;

                for (var i = 0; i < c; i++)
                {
                    Type pt = pi[i].ParameterType;
                    if (pi[i].IsOut || pt.IsByRef)
                    {
                        v = Converter.ToPython(binding.args[i], pt);
                        Runtime.PyTuple_SetItem(t, n, v);
                        n++;
                    }
                }

                if (binding.outs == 1 && mi.ReturnType == typeof(void))
                {
                    v = Runtime.PyTuple_GetItem(t, 1);
                    Runtime.XIncref(v);
                    Runtime.XDecref(t);
                    return v;
                }

                return t;
            }

            return Converter.ToPython(result, mi.ReturnType);
        }
    }


    /// <summary>
    /// Utility class to sort method info by parameter type precedence.
    /// </summary>
    internal class MethodSorter : IComparer
    {
        int IComparer.Compare(object m1, object m2)
        {
            int p1 = MethodBinder.GetPrecedence((MethodBase)m1);
            int p2 = MethodBinder.GetPrecedence((MethodBase)m2);
            if (p1 < p2)
            {
                return -1;
            }
            if (p1 > p2)
            {
                return 1;
            }
            return 0;
        }
    }


    /// <summary>
    /// A Binding is a utility instance that bundles together a MethodInfo
    /// representing a method to call, a (possibly null) target instance for
    /// the call, and the arguments for the call (all as managed values).
    /// </summary>
    internal class Binding
    {
        public MethodBase info;
        public object[] args;
        public object inst;
        public int outs;

        internal Binding(MethodBase info, object inst, object[] args, int outs)
        {
            this.info = info;
            this.inst = inst;
            this.args = args;
            this.outs = outs;
        }
    }
}
