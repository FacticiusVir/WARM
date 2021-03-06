﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Keeper.Warm
{
    public interface ICodeGenerator
    {
        void Emit(Opcode opcode);
        void Emit(Opcode opcode, int argument);
        void Emit(Opcode opcode, MethodToken methodToken);
        void Emit(Opcode opcode, Label label);
        void Emit(Opcode opcode, Local local);
        Label DefineLabel();
        void MarkLabel(Label label);
        Local DefineLocal();
    }

    public class Local
    {
        internal Local(int index)
        {
            this.Index = index;
        }

        internal int Index
        {
            get;
            private set;
        }
    }

    public class Label
    {
        internal Label()
        {
        }
    }

    public class CodeGenerator
        : ICodeGenerator
    {
        private class LabelInfo
        {
            public List<int> References = new List<int>();
            public int? Mark;
        }

        private List<int> code = new List<int>();
        private Stack<Dictionary<Label, LabelInfo>> contexts = new Stack<Dictionary<Label, LabelInfo>>();
        private int nextFreeLocal;
        private int argumentCount;
        private List<MethodToken> tokens = new List<MethodToken>();
        private Dictionary<MethodToken, int> tokenLookup = new Dictionary<MethodToken, int>();

        public CodeGenerator(MethodToken token)
        {
            this.argumentCount = token.Arity;

            this.contexts.Push(new Dictionary<Label, LabelInfo>());
        }

        public void Emit(Opcode opcode)
        {
            this.code.Add((int)opcode);
        }

        public void Emit(Opcode opcode, int argument)
        {
            this.code.Add((int)opcode);
            this.code.Add(argument);
        }

        public void Emit(Opcode opcode, MethodToken methodToken)
        {
            int methodTokenIndex;

            if(!this.tokenLookup.TryGetValue(methodToken, out methodTokenIndex))
            {
                methodTokenIndex = this.tokens.Count;
                this.tokens.Add(methodToken);
                this.tokenLookup.Add(methodToken, methodTokenIndex);
            }

            this.Emit(opcode, methodTokenIndex);
        }

        public void Emit(Opcode opcode, Label label)
        {
            this.code.Add((int)opcode);

            var context = this.contexts.Peek();

            var info = context[label];

            info.References.Add(this.code.Count);

            this.code.Add(0);
        }

        public void Emit(Opcode opcode, Local local)
        {
            this.code.Add((int)opcode);
            this.code.Add(local.Index);
        }

        public Label DefineLabel()
        {
            var result = new Label();

            var context = this.contexts.Peek();

            context.Add(result, new LabelInfo());

            return result;
        }

        public void MarkLabel(Label label)
        {
            var context = this.contexts.Peek();

            var info = context[label];

            info.Mark = this.code.Count;
        }

        public IEnumerable<int> Generate()
        {
            this.ApplyLabelsForCurrentContext();

            foreach (var item in this.code)
            {
                yield return item;
            }
            
            yield return (int)Opcode.Proceed;
        }

        public MethodToken[] GetMethodTokens()
        {
            return this.tokens.ToArray();
        }

        private void ApplyLabelsForCurrentContext()
        {
            foreach (var label in this.contexts.Peek().Values)
            {
                foreach (int referencePointer in label.References)
                {
                    this.code[referencePointer] = label.Mark.Value - (referencePointer - 1);
                }
            }
        }

        public Local DefineLocal()
        {
            return new Local(this.nextFreeLocal++);
        }
    }
}
