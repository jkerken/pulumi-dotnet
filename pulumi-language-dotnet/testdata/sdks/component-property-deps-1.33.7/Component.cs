// *** WARNING: this file was generated by pulumi-language-dotnet. ***
// *** Do not edit by hand unless you're certain you know what you are doing! ***

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Pulumi.Serialization;

namespace Pulumi.ComponentPropertyDeps
{
    /// <summary>
    /// A component resource that accepts a list of resources. The construct request's property dependencies are returned as an output.
    /// </summary>
    [ComponentPropertyDepsResourceType("component-property-deps:index:Component")]
    public partial class Component : global::Pulumi.ComponentResource
    {
        [Output("propertyDeps")]
        public Output<ImmutableDictionary<string, ImmutableArray<string>>> PropertyDeps { get; private set; } = null!;


        /// <summary>
        /// Create a Component resource with the given unique name, arguments, and options.
        /// </summary>
        ///
        /// <param name="name">The unique name of the resource</param>
        /// <param name="args">The arguments used to populate this resource's properties</param>
        /// <param name="options">A bag of options that control this resource's behavior</param>
        public Component(string name, ComponentArgs args, ComponentResourceOptions? options = null)
            : base("component-property-deps:index:Component", name, args ?? new ComponentArgs(), MakeResourceOptions(options, ""), remote: true)
        {
        }

        private static ComponentResourceOptions MakeResourceOptions(ComponentResourceOptions? options, Input<string>? id)
        {
            var defaultOptions = new ComponentResourceOptions
            {
                Version = Utilities.Version,
            };
            var merged = ComponentResourceOptions.Merge(defaultOptions, options);
            // Override the ID if one was specified for consistency with other language SDKs.
            merged.Id = id ?? merged.Id;
            return merged;
        }

        /// <summary>
        /// The `refs` method of the `Component` component resource. Returns the call request's property dependencies.
        /// </summary>
        public global::Pulumi.Output<ComponentRefsResult> Refs(ComponentRefsArgs args)
            => global::Pulumi.Deployment.Instance.Call<ComponentRefsResult>("component-property-deps:index:Component/refs", args ?? new ComponentRefsArgs(), this);
    }

    public sealed class ComponentArgs : global::Pulumi.ResourceArgs
    {
        [Input("resource", required: true)]
        public Pulumi.ComponentPropertyDeps.Custom Resource { get; set; } = null!;

        [Input("resourceList", required: true)]
        private List<Pulumi.ComponentPropertyDeps.Custom>? _resourceList;
        public List<Pulumi.ComponentPropertyDeps.Custom> ResourceList
        {
            get => _resourceList ?? (_resourceList = new List<Pulumi.ComponentPropertyDeps.Custom>());
            set => _resourceList = value;
        }

        [Input("resourceMap", required: true)]
        private Dictionary<string, Pulumi.ComponentPropertyDeps.Custom>? _resourceMap;
        public Dictionary<string, Pulumi.ComponentPropertyDeps.Custom> ResourceMap
        {
            get => _resourceMap ?? (_resourceMap = new Dictionary<string, Pulumi.ComponentPropertyDeps.Custom>());
            set => _resourceMap = value;
        }

        public ComponentArgs()
        {
        }
        public static new ComponentArgs Empty => new ComponentArgs();
    }

    /// <summary>
    /// The set of arguments for the <see cref="Component.Refs"/> method.
    /// </summary>
    public sealed class ComponentRefsArgs : global::Pulumi.CallArgs
    {
        [Input("resource", required: true)]
        public Pulumi.ComponentPropertyDeps.Custom Resource { get; set; } = null!;

        [Input("resourceList", required: true)]
        private List<Input<Pulumi.ComponentPropertyDeps.Custom>>? _resourceList;
        public List<Input<Pulumi.ComponentPropertyDeps.Custom>> ResourceList
        {
            get => _resourceList ?? (_resourceList = new List<Input<Pulumi.ComponentPropertyDeps.Custom>>());
            set => _resourceList = value;
        }

        [Input("resourceMap", required: true)]
        private Dictionary<string, Input<Pulumi.ComponentPropertyDeps.Custom>>? _resourceMap;
        public Dictionary<string, Input<Pulumi.ComponentPropertyDeps.Custom>> ResourceMap
        {
            get => _resourceMap ?? (_resourceMap = new Dictionary<string, Input<Pulumi.ComponentPropertyDeps.Custom>>());
            set => _resourceMap = value;
        }

        public ComponentRefsArgs()
        {
        }
        public static new ComponentRefsArgs Empty => new ComponentRefsArgs();
    }

    /// <summary>
    /// The results of the <see cref="Component.Refs"/> method.
    /// </summary>
    [OutputType]
    public sealed class ComponentRefsResult
    {
        public readonly ImmutableDictionary<string, ImmutableArray<string>> Result;

        [OutputConstructor]
        private ComponentRefsResult(ImmutableDictionary<string, ImmutableArray<string>> result)
        {
            Result = result;
        }
    }
}
