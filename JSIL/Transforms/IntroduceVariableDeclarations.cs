﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ICSharpCode.Decompiler.ILAst;
using JSIL.Ast;
using JSIL.Internal;

namespace JSIL.Transforms {
    public class IntroduceVariableDeclarations : JSAstVisitor {
        public readonly HashSet<JSVariable> ToDeclare = new HashSet<JSVariable>();

        public readonly IDictionary<string, JSVariable> Variables;
        public readonly ITypeInfoSource TypeInfo;

        public IntroduceVariableDeclarations (IDictionary<string, JSVariable> variables, ITypeInfoSource typeInfo) {
            Variables = variables;
            TypeInfo = typeInfo;
        }

        public void VisitNode (JSFunctionExpression fn) {
            if (Stack.Count != 0) {
                var nested = new IntroduceVariableDeclarations(fn.AllVariables, TypeInfo);
                nested.Visit(fn);
                return;
            }

            var existingDeclarations = new HashSet<string>(
                fn.AllChildrenRecursive.OfType<JSVariableDeclarationStatement>()
                .SelectMany(
                    (vds) => 
                        from vd in vds.Declarations 
                        select ((JSVariable)vd.Left).Identifier
                ).Union(
                    from tcb in fn.AllChildrenRecursive.OfType<JSTryCatchBlock>()
                    where tcb.CatchVariable != null
                    select tcb.CatchVariable.Identifier
                )
            );

            foreach (var v_ in from v in Variables.Values
                               where !v.IsParameter &&
                                     !existingDeclarations.Contains(v.Identifier) &&
                                     !TypeInfo.IsIgnored(v.Type)
                               select v) 
            {
                ToDeclare.Add(v_);
            }

            VisitChildren(fn);

            if (ToDeclare.Count > 0)
                fn.Body.Statements.Insert(
                    0, new JSVariableDeclarationStatement(
                        (from v in ToDeclare
                         select new JSBinaryOperatorExpression(
                            JSOperator.Assignment, v,
                            JSLiteral.DefaultValue(v.Type), 
                            v.Type
                        )).ToArray()
                    )
                );
        }

        public void VisitNode (JSExpressionStatement es) {
            var boe = es.Expression as JSBinaryOperatorExpression;

            if (boe != null) {
                var isAssignment = boe.Operator == JSOperator.Assignment;
                var leftVar = boe.Left as JSVariable;

                if (
                    (leftVar != null) && isAssignment &&
                    (boe.Right.IsConstant) && (!leftVar.IsReference) &&
                    (!leftVar.IsParameter)
                ) {
                    if (ToDeclare.Contains(leftVar)) {
                        ToDeclare.Remove(leftVar);

                        ParentNode.ReplaceChild(es, new JSVariableDeclarationStatement(
                            boe
                        ));

                        VisitChildren(boe);
                        return;
                    }
                }
            }

            VisitChildren(es.Expression);
        }
    }
}