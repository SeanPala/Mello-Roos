/****** Object:  Table [dbo].[Rate]    Script Date: 8/12/2026 1:23:07 PM ******/

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[Rate](
               [ID] [int] IDENTITY(1,1) NOT NULL,
               [debt_id] [int] NOT NULL,
               [rate_type] [int] NULL,
               [display_order] [int] NULL,
               [class_id] [int] NOT NULL,
               [class_name] [varchar](100) NOT NULL,
               [class_description] [varchar](250) NULL,
               [class_other] [varchar](100) NULL,
               [land_use] [varchar](100) NULL,
               [land_use_type] [int] NULL,
               [initial_roll_year] [numeric](4, 0) NULL,
               [max_tax_rate] [numeric](18, 5) NULL,
               [max_tax_unit] [varchar](100) NULL,
               [max_tax_qty] [numeric](18, 5) NULL,
               [max_tax_qty_source] [varchar](50) NULL,
               [current_roll_year] [numeric](4, 0) NULL,
               [current_max_tax_rate] [numeric](18, 5) NULL,
               [max_tax_text] [varchar](500) NULL,
               [backup_tax_flag] [bit] NOT NULL,
               [backup_tax_rate] [numeric](18, 5) NULL,
               [current_backup_tax_rate] [numeric](18, 5) NULL,
               [backup_tax_text] [varchar](1000) NULL,
               [nost_type_id] [int] NULL,
               [RecordCreatedBy] [int] NULL,
               [RecordCreatedDate] [smalldatetime] NULL,
               [RecordModifiedBy] [int] NULL,
               [RecordModifiedDate] [smalldatetime] NULL,
               [RecordLockedFlag] [bit] NULL,
               [RecordLockedBy] [int] NULL,
               [RecordLockedDate] [smalldatetime] NULL,
CONSTRAINT [PK_DebtRate] PRIMARY KEY CLUSTERED
(
               [ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_backup_tax_flag]  DEFAULT ((0)) FOR [backup_tax_flag]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_nost_type_id]  DEFAULT ((1)) FOR [nost_type_id]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_RecordCreatedBy]  DEFAULT ((0)) FOR [RecordCreatedBy]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_RecordCreatedDate]  DEFAULT (getdate()) FOR [RecordCreatedDate]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_RecordModifiedBy]  DEFAULT ((0)) FOR [RecordModifiedBy]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_RecordModifiedDate]  DEFAULT (getdate()) FOR [RecordModifiedDate]
GO

ALTER TABLE [dbo].[Rate] ADD  CONSTRAINT [DF_Rate_RecordLockedFlag]  DEFAULT ((0)) FOR [RecordLockedFlag]
GO

ALTER TABLE [dbo].[Rate]  WITH CHECK ADD  CONSTRAINT [FK_Rate_Debt] FOREIGN KEY([debt_id])
REFERENCES [dbo].[Debt] ([DebtId])
GO

ALTER TABLE [dbo].[Rate] CHECK CONSTRAINT [FK_Rate_Debt]
GO
