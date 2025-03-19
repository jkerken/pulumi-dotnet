// *** WARNING: this file was generated by pulumi-language-dotnet. ***
// *** Do not edit by hand unless you're certain you know what you are doing! ***

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Pulumi.Serialization;

namespace Pulumi.Goodbye
{
    [GoodbyeResourceType("goodbye:index:GoodbyeComponent")]
    public partial class GoodbyeComponent : global::Pulumi.ComponentResource
    {
        [Output("parameterValue")]
        public Output<string> ParameterValue { get; private set; } = null!;


        /// <summary>
        /// Create a GoodbyeComponent resource with the given unique name, arguments, and options.
        /// </summary>
        ///
        /// <param name="name">The unique name of the resource</param>
        /// <param name="args">The arguments used to populate this resource's properties</param>
        /// <param name="options">A bag of options that control this resource's behavior</param>
        public GoodbyeComponent(string name, GoodbyeComponentArgs? args = null, ComponentResourceOptions? options = null)
            : base("goodbye:index:GoodbyeComponent", name, args ?? new GoodbyeComponentArgs(), MakeResourceOptions(options, ""), remote: true, Utilities.PackageParameterization())
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
    }

    public sealed class GoodbyeComponentArgs : global::Pulumi.ResourceArgs
    {
        public GoodbyeComponentArgs()
        {
        }
        public static new GoodbyeComponentArgs Empty => new GoodbyeComponentArgs();
    }
}
