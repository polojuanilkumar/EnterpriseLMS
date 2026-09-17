using System;
using System.Collections.Generic;
using System.Text;

namespace LMS.Domain.Common
{
    public abstract class BaseEntity
    {
        public Guid Id { get; protected set; }

        public DateTime CreatedOn { get; protected set; }

        public DateTime? ModifiedOn { get; protected set; }

        protected BaseEntity()
        {
            Id = Guid.NewGuid();
            CreatedOn = DateTime.UtcNow;
        }

        public void SetModified()
        {
            ModifiedOn = DateTime.UtcNow;
        }
    }
}
