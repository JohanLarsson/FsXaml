﻿namespace FsXaml

open System
open System.Reflection
open System.Windows
open System.Windows.Data
open System.Windows.Input
open System.Windows.Markup

module ServiceProvider = 
    let private getService<'t> (serviceProvider : IServiceProvider) = serviceProvider.GetService typeof<'t> :?> 't
    let ProvideValueTarget serviceProvider = getService<IProvideValueTarget> serviceProvider
    let XamlTypeResolver serviceProvider = getService<IXamlTypeResolver> serviceProvider

module Reflection = 
    let tryGetMethod (t: Type) methodName =
        let tryGetMethod (t: Type) methodName =
            let mi = t.GetMethod methodName
            if obj.ReferenceEquals(mi, null) then None
            else Some(mi)
        t.GetInterfaces()
        |> Array.tryPick (fun i -> tryGetMethod i methodName)

    let private tryGetAnyMethod source methodName =
        let t = source.GetType()
        let mi = t.GetMethod methodName
        if obj.ReferenceEquals(mi, null) then
            tryGetMethod t methodName
        else Some(mi)

    let invoke source methodName arg = 
        let mi = tryGetAnyMethod source methodName
        match mi with
        | Some mi -> mi.Invoke(source, [| arg |])
        | None -> failwithf "Could not find method: %s"  methodName

type ToArrayConverter() = 
    static member Default = ToArrayConverter()
    interface IMultiValueConverter with
        member x.Convert(values : obj [], _, _, _) : obj = 
            // clone is needed here, if we return the same instance WPF ~fixes~ it by replaing it with an array of nulls.
            values.Clone()
        member x.ConvertBack(_, _, _, _) : obj [] = failwith "Not supported"

[<MarkupExtensionReturnType(typeof<RoutedEventHandler>)>]
type HandlerExtension(observerBinding : Binding, map : obj) as me = 
    inherit MarkupExtension()
    let observerBinding = observerBinding
    let map = map
    static let HandlersProperty = 
        DependencyProperty.RegisterAttached
            ("Handlers", typeof<ResizeArray<HandlerExtension>>, typeof<HandlerExtension>, PropertyMetadata(null))
    static let ObserversProperty = 
        DependencyProperty.RegisterAttached
            ("Observers", typeof<obj []>, typeof<HandlerExtension>, PropertyMetadata(null))
    
    let bindObserver (element : FrameworkElement) = 
        let mutable handlers = element.GetValue(HandlersProperty) :?> ResizeArray<HandlerExtension>
        if obj.ReferenceEquals(handlers, null) then 
            handlers <- ResizeArray<HandlerExtension>()
            element.SetValue(HandlersProperty, handlers)
        handlers.Add me
        let binding = MultiBinding()
        binding.Converter <- ToArrayConverter.Default
        for handler in handlers do
            binding.Bindings.Add handler.ObserverBinding
        BindingOperations.SetBinding(element, ObserversProperty, binding)
    
    let getObserver (element : FrameworkElement) = 
        let handlers = element.GetValue(HandlersProperty) :?> ResizeArray<HandlerExtension>
        let index = handlers.IndexOf(me)
        let observers = element.GetValue(ObserversProperty) :?> obj []
        observers.[index]
    
    let onEvent (sender : obj) (e : RoutedEventArgs) = 
        let mapped = Reflection.invoke map "Invoke" e
        let observer = getObserver (sender :?> FrameworkElement)
        Reflection.invoke observer "OnNext" mapped |> ignore
    
    override this.ProvideValue(serviceProvider : IServiceProvider) = 
        let provideValueTarget = ServiceProvider.ProvideValueTarget serviceProvider
        bindObserver (provideValueTarget.TargetObject :?> FrameworkElement) |> ignore
        RoutedEventHandler onEvent :> _
    
    member __.ObserverBinding = observerBinding

[<MarkupExtensionReturnType(typeof<MethodInfo>)>]
type FunctionExtension(name : String) = 
    inherit MarkupExtension()
    let name = name
    override this.ProvideValue(serviceProvider : IServiceProvider) =
        let resolver = ServiceProvider.XamlTypeResolver serviceProvider
        if obj.ReferenceEquals(resolver, null) then null
        else
            let m = System.Text.RegularExpressions.Regex.Match(name, @"(?<qn>\w+:\w+)\.(?<method>\w+)")
            if m.Success then
                let t = resolver.Resolve(m.Groups.["qn"].Value)
                let methodName = m.Groups.["method"].Value
                t.GetMethod(methodName, BindingFlags.Static ||| BindingFlags.Public ||| BindingFlags.NonPublic) :>_
            else null
                